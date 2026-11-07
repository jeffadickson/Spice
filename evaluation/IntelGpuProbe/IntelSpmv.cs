#:property PublishAot=false
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

// A deterministic sparse kernel experiment. GPU selection requires a real GPU
// reporting FP64. No CPU OpenCL fallback is allowed to count as GPU execution.
const int repetitions = 9;
const int seed = 18117;
const string source = """
    #pragma OPENCL EXTENSION cl_khr_fp64 : enable
    #pragma OPENCL FP_CONTRACT OFF
    __kernel void spmv(__global const int* offsets, __global const int* columns,
                       __global const double* coefficients, __global const double* x,
                       __global double* y) {
        size_t row = get_global_id(0);
        size_t index = get_global_size(0) * get_global_id(1);
        double total = 0.0;
        for (int k = offsets[row]; k < offsets[row + 1]; ++k)
            total += coefficients[k] * x[index + columns[k]];
        y[index + row] = total;
    }
    """;

if (args is not [] && args is not ["--self-test"])
    throw new ArgumentException("Usage: IntelSpmv.cs [--self-test]");
if (args is ["--self-test"])
{
    CheckEqual([1.0], [1.0]);
    foreach (var wrong in new[] { new[] { 1.0 + 1e-10 }, new[] { double.NaN }, Array.Empty<double>() })
    {
        try { CheckEqual([1.0], wrong); }
        catch (InvalidDataException) { continue; }
        throw new InvalidOperationException("The comparison accepted a wrong result.");
    }
    Console.WriteLine("FP64 result, corrupted value, NaN and shape checks passed.");
    return;
}

var hostSetup = Stopwatch.StartNew();
var (platform, device) = OpenCl.FindIntelGpu();
using var resources = new Resources();
var context = resources.Add(OpenCl.clCreateContext(0, 1, ref device, 0, 0, out var status),
    status, OpenCl.clReleaseContext, "create context");
var queue = resources.Add(OpenCl.clCreateCommandQueue(context, device, 0, out status),
    status, OpenCl.clReleaseCommandQueue, "create in-order queue");
var sourcePointer = Marshal.StringToCoTaskMemUTF8(source);
nint program;
try
{
    program = resources.Add(OpenCl.clCreateProgramWithSource(context, 1, ref sourcePointer, 0, out status),
        status, OpenCl.clReleaseProgram, "create program");
}
finally { Marshal.FreeCoTaskMem(sourcePointer); }
status = OpenCl.clBuildProgram(program, 1, ref device, "-cl-std=CL1.2", 0, 0);
if (status != 0)
{
    var log = new byte[65536];
    OpenCl.clGetProgramBuildInfo(program, device, 0x1183, (nuint)log.Length, log, out var size);
    throw new InvalidOperationException($"OpenCL build failed ({status}): {Encoding.UTF8.GetString(log, 0, (int)Math.Min(size, (nuint)log.Length)).TrimEnd('\0')}");
}
var kernel = resources.Add(OpenCl.clCreateKernel(program, "spmv", out status),
    status, OpenCl.clReleaseKernel, "create kernel");
hostSetup.Stop();

var results = new List<object>();
foreach (int rows in new[] { 256, 4096, 65536 })
foreach (int batch in new[] { 1, 16 })
{
    // A fixed-width banded CSR fixture models local stencil connectivity.
    // Binary fractions plus tiny perturbations make accidental FP32 detectable.
    int[] offsets = new int[rows + 1], columns = new int[rows * 5];
    double[] values = new double[columns.Length], input = new double[rows * batch];
    var random = new Random(seed);
    int[] neighbors = [-17, -1, 0, 1, 17];
    for (int row = 0; row < rows; row++)
    {
        offsets[row] = row * neighbors.Length;
        for (int k = 0; k < neighbors.Length; k++)
        {
            columns[offsets[row] + k] = (row + neighbors[k] + rows) % rows;
            values[offsets[row] + k] = k == 2 ? 4.5 : -1.0;
        }
    }
    offsets[rows] = columns.Length;
    for (int i = 0; i < input.Length; i++)
        input[i] = random.Next(-1024, 1025) / 1024.0 + ((i % 7) + 1) * Math.ScaleB(1.0, -38);

    double[] expected = new double[input.Length], cpu = new double[input.Length], gpu = new double[input.Length];
    // Compute the reference directly from the stencil, independently of CSR
    // indexing and the CPU/GPU kernel loops.
    for (int rhs = 0; rhs < batch; rhs++)
    for (int row = 0; row < rows; row++)
    {
        var index = rhs * rows;
        expected[index + row] = 4.5 * input[index + row]
            - input[index + (row - 17 + rows) % rows] - input[index + (row - 1 + rows) % rows]
            - input[index + (row + 1) % rows] - input[index + (row + 17) % rows];
    }

    CpuMultiply();
    CheckEqual(expected, cpu);
    var setup = Stopwatch.StartNew();
    using var buffers = new Resources();
    nint NewBuffer(nuint bytes, ulong flags) => buffers.Add(OpenCl.clCreateBuffer(context, flags, bytes, 0, out var error),
        error, OpenCl.clReleaseMemObject, "create buffer");
    var offsetsBuffer = NewBuffer((nuint)offsets.Length * sizeof(int), 4);
    var columnsBuffer = NewBuffer((nuint)columns.Length * sizeof(int), 4);
    var coefficientsBuffer = NewBuffer((nuint)values.Length * sizeof(double), 4);
    var inputBuffer = NewBuffer((nuint)input.Length * sizeof(double), 4);
    var outputBuffer = NewBuffer((nuint)gpu.Length * sizeof(double), 2);
    Upload(offsetsBuffer, offsets, sizeof(int));
    Upload(columnsBuffer, columns, sizeof(int));
    Upload(coefficientsBuffer, values, sizeof(double));
    var argsForKernel = new[] { offsetsBuffer, columnsBuffer, coefficientsBuffer, inputBuffer, outputBuffer };
    for (uint arg = 0; arg < argsForKernel.Length; arg++)
        OpenCl.Check(OpenCl.clSetKernelArg(kernel, arg, (nuint)IntPtr.Size, ref argsForKernel[arg]), "set kernel argument");
    nuint[] global = [(nuint)rows, (nuint)batch];
    setup.Stop();

    var firstRoundtrip = Stopwatch.StartNew();
    Roundtrip();
    firstRoundtrip.Stop();
    double maxError = CheckEqual(expected, gpu);
    var cpuTimes = new double[repetitions];
    var gpuTimes = new double[repetitions];
    for (int trial = 0; trial < repetitions; trial++)
    {
        // Change the right-hand side on every trial, so a stale GPU result
        // cannot pass. Update the independent stencil reference outside timing.
        input[0] += 0.125;
        foreach (int offset in neighbors)
            expected[(rows - offset) % rows] += (offset == 0 ? 4.5 : -1.0) * 0.125;
        var start = Stopwatch.GetTimestamp();
        CpuMultiply();
        cpuTimes[trial] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = Stopwatch.GetTimestamp();
        Roundtrip();
        gpuTimes[trial] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        maxError = Math.Max(maxError, CheckEqual(expected, gpu));
        CheckEqual(expected, cpu);
    }
    Array.Sort(cpuTimes);
    Array.Sort(gpuTimes);
    results.Add(new
    {
        rows, batch, nonzeros = values.Length, checked_values = (repetitions + 1) * gpu.Length,
        max_absolute_error = maxError, gpu_buffers_and_matrix_upload_ms = setup.Elapsed.TotalMilliseconds,
        gpu_first_roundtrip_ms = firstRoundtrip.Elapsed.TotalMilliseconds,
        cpu_median_ms = cpuTimes[repetitions / 2], gpu_roundtrip_median_ms = gpuTimes[repetitions / 2],
        cpu_range_ms = new[] { cpuTimes[0], cpuTimes[^1] },
        gpu_roundtrip_range_ms = new[] { gpuTimes[0], gpuTimes[^1] },
    });

    void CpuMultiply()
    {
        for (int rhs = 0; rhs < batch; rhs++)
        for (int row = 0; row < rows; row++)
        {
            var index = rhs * rows;
            double total = 0;
            for (int k = offsets[row]; k < offsets[row + 1]; k++)
                total += values[k] * input[index + columns[k]];
            cpu[index + row] = total;
        }
    }

    void Upload(nint buffer, Array data, int itemBytes)
    {
        var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            OpenCl.Check(OpenCl.clEnqueueWriteBuffer(queue, buffer, 1, 0, (nuint)data.Length * (nuint)itemBytes,
                pinned.AddrOfPinnedObject(), 0, 0, 0), "upload buffer");
        }
        finally { pinned.Free(); }
    }

    void Roundtrip()
    {
        Upload(inputBuffer, input, sizeof(double));
        OpenCl.Check(OpenCl.clEnqueueNDRangeKernel(queue, kernel, 2, 0, global, 0, 0, 0, 0), "dispatch FP64 kernel");
        // The blocking read waits for the in-order queue, including the kernel.
        var pinned = GCHandle.Alloc(gpu, GCHandleType.Pinned);
        try
        {
            OpenCl.Check(OpenCl.clEnqueueReadBuffer(queue, outputBuffer, 1, 0, (nuint)gpu.Length * sizeof(double),
                pinned.AddrOfPinnedObject(), 0, 0, 0), "read result");
        }
        finally { pinned.Free(); }
    }
}
Console.WriteLine(JsonSerializer.Serialize(new
{
    probe = "Intel OpenCL FP64 sparse matrix-vector multiply",
    device = OpenCl.DeviceText(device, 0x102B),
    driver = OpenCl.DeviceText(device, 0x102D),
    opencl = OpenCl.DeviceText(device, 0x102F),
    runtime = RuntimeInformation.FrameworkDescription,
    repetitions, seed, host_context_and_program_setup_ms = hostSetup.Elapsed.TotalMilliseconds,
    timings = "GPU steady time includes RHS upload, dispatch, synchronization and readback. CSR matrix remains resident.",
    results
}, new JsonSerializerOptions { WriteIndented = true }));

static double CheckEqual(double[] expected, double[] actual)
{
    if (expected.Length != actual.Length) throw new InvalidDataException("Wrong result length.");
    double max = 0;
    for (int i = 0; i < expected.Length; i++)
    {
        double error = Math.Abs(expected[i] - actual[i]);
        if (!double.IsFinite(error) || error > 2e-14 * Math.Max(1.0, Math.Abs(expected[i])))
            throw new InvalidDataException($"FP64 mismatch at {i}: expected {expected[i]:R}, actual {actual[i]:R}.");
        max = Math.Max(max, error);
    }
    return max;
}

// OpenCL retains references internally. Release explicitly in reverse order,
// including on a failed build or numeric validation.
sealed class Resources : IDisposable
{
    private readonly Stack<(nint Handle, Func<nint, int> Release)> owned = new();
    public nint Add(nint handle, int status, Func<nint, int> release, string operation)
    {
        OpenCl.Check(status, operation);
        if (handle == 0) throw new InvalidOperationException($"{operation} returned a null handle.");
        owned.Push((handle, release));
        return handle;
    }
    public void Dispose()
    {
        while (owned.TryPop(out var item)) item.Release(item.Handle);
    }
}

static class OpenCl
{
    private const string Library = "OpenCL.dll";
    public static void Check(int status, string operation)
    {
        if (status != 0) throw new InvalidOperationException($"OpenCL {operation} failed ({status}).");
    }
    public static (nint Platform, nint Device) FindIntelGpu()
    {
        Check(clGetPlatformIDs(0, null, out var count), "count platforms");
        nint[] platforms = new nint[count];
        Check(clGetPlatformIDs(count, platforms, out _), "list platforms");
        foreach (nint platform in platforms)
        {
            int status = clGetDeviceIDs(platform, 4, 0, null, out count); // CL_DEVICE_TYPE_GPU
            if (status == -1) continue; // CL_DEVICE_NOT_FOUND
            Check(status, "count GPU devices");
            nint[] devices = new nint[count];
            Check(clGetDeviceIDs(platform, 4, count, devices, out _), "list GPU devices");
            foreach (nint device in devices)
                if (DeviceText(device, 0x102C).Contains("Intel", StringComparison.OrdinalIgnoreCase) &&
                    DeviceText(device, 0x1030).Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("cl_khr_fp64"))
                    return (platform, device);
        }
        throw new PlatformNotSupportedException("No Intel GPU exposing cl_khr_fp64 through the installed Windows OpenCL ICD.");
    }
    public static string DeviceText(nint device, uint property)
    {
        Check(clGetDeviceInfo(device, property, 0, null, out nuint bytes), "query device info size");
        if (bytes == 0 || bytes > 65536) throw new InvalidDataException("Unexpected OpenCL device string size.");
        byte[] text = new byte[(int)bytes];
        Check(clGetDeviceInfo(device, property, bytes, text, out _), "query device info");
        return Encoding.UTF8.GetString(text).TrimEnd('\0');
    }

    [DllImport(Library)] private static extern int clGetPlatformIDs(uint count, [Out] nint[]? platforms, out uint total);
    [DllImport(Library)] private static extern int clGetDeviceIDs(nint platform, ulong type, uint count, [Out] nint[]? devices, out uint total);
    [DllImport(Library)] private static extern int clGetDeviceInfo(nint device, uint property, nuint size, [Out] byte[]? data, out nuint actualSize);
    [DllImport(Library)] public static extern nint clCreateContext(nint properties, uint count, ref nint device, nint callback, nint user, out int error);
    [DllImport(Library)] public static extern nint clCreateCommandQueue(nint context, nint device, ulong properties, out int error);
    [DllImport(Library)] public static extern nint clCreateProgramWithSource(nint context, uint count, ref nint source, nint lengths, out int error);
    [DllImport(Library, CharSet = CharSet.Ansi, ExactSpelling = true)] public static extern int clBuildProgram(nint program, uint count, ref nint device, string options, nint callback, nint user);
    [DllImport(Library)] public static extern int clGetProgramBuildInfo(nint program, nint device, uint property, nuint size, [Out] byte[] data, out nuint actualSize);
    [DllImport(Library, CharSet = CharSet.Ansi, ExactSpelling = true)] public static extern nint clCreateKernel(nint program, string name, out int error);
    [DllImport(Library)] public static extern nint clCreateBuffer(nint context, ulong flags, nuint size, nint host, out int error);
    [DllImport(Library)] public static extern int clSetKernelArg(nint kernel, uint index, nuint size, ref nint value);
    [DllImport(Library)] public static extern int clEnqueueWriteBuffer(nint queue, nint buffer, uint blocking, nuint offset, nuint size, nint data, uint waitCount, nint waits, nint @event);
    [DllImport(Library)] public static extern int clEnqueueReadBuffer(nint queue, nint buffer, uint blocking, nuint offset, nuint size, nint data, uint waitCount, nint waits, nint @event);
    [DllImport(Library)] public static extern int clEnqueueNDRangeKernel(nint queue, nint kernel, uint dimensions, nint offset, nuint[] globalSize, nint localSize, uint waitCount, nint waits, nint @event);
    [DllImport(Library)] public static extern int clReleaseMemObject(nint buffer);
    [DllImport(Library)] public static extern int clReleaseKernel(nint kernel);
    [DllImport(Library)] public static extern int clReleaseProgram(nint program);
    [DllImport(Library)] public static extern int clReleaseCommandQueue(nint queue);
    [DllImport(Library)] public static extern int clReleaseContext(nint context);
}
