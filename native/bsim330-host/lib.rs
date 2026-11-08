//! Isolates the opt-in native model ABI from the safe solver crate.
#[cfg(all(any(windows, target_os = "linux"), target_arch = "x86_64"))]
mod native {
    use std::{ffi::c_void, path::PathBuf, sync::OnceLock};

    #[cfg(windows)]
    type PathUnit = u16;
    #[cfg(target_os = "linux")]
    type PathUnit = u8;
    type Open = unsafe extern "C" fn(*const PathUnit) -> i32;
    type Trial = unsafe extern "C" fn(*const f64, *const f64, *mut f64) -> i32;
    type FdTrial = unsafe extern "C" fn(*const f64, *mut f64) -> i32;
    static FD_TRIAL: OnceLock<Result<std::sync::Mutex<FdTrial>, String>> = OnceLock::new();
    static TRIAL: OnceLock<Result<Trial, String>> = OnceLock::new();

    #[cfg(windows)]
    #[link(name = "kernel32")]
    unsafe extern "system" {
        fn LoadLibraryExW(path: *const u16, file: *mut c_void, flags: u32) -> *mut c_void;
        fn GetProcAddress(library: *mut c_void, name: *const u8) -> *mut c_void;
    }

    #[cfg(target_os = "linux")]
    #[link(name = "dl")]
    unsafe extern "C" {
        fn dlopen(path: *const u8, flags: i32) -> *mut c_void;
        fn dlsym(library: *mut c_void, name: *const u8) -> *mut c_void;
    }

    unsafe fn open_host(path: *const PathUnit) -> *mut c_void {
        #[cfg(windows)]
        return unsafe { LoadLibraryExW(path, std::ptr::null_mut(), 0x800) };
        // RTLD_NOW | RTLD_LOCAL resolves host symbols without exporting them.
        #[cfg(target_os = "linux")]
        return unsafe { dlopen(path, 2) };
    }

    unsafe fn symbol(library: *mut c_void, name: &std::ffi::CStr) -> *mut c_void {
        #[cfg(windows)]
        return unsafe { GetProcAddress(library, name.as_ptr().cast()) };
        #[cfg(target_os = "linux")]
        return unsafe { dlsym(library, name.as_ptr().cast()) };
    }

    fn path(name: &str) -> Result<Vec<PathUnit>, String> {
        let path = std::env::var_os(name)
            .map(PathBuf::from)
            .filter(|p| p.is_absolute() && p.is_file())
            .ok_or_else(|| format!("{name} must select an absolute trusted native file"))?;
        #[cfg(windows)]
        {
            use std::os::windows::ffi::OsStrExt;
            Ok(path.as_os_str().encode_wide().chain([0]).collect())
        }
        #[cfg(target_os = "linux")]
        {
            use std::os::unix::ffi::OsStrExt;
            Ok(path
                .as_os_str()
                .as_bytes()
                .iter()
                .copied()
                .chain([0])
                .collect())
        }
    }

    fn bind() -> Result<Trial, String> {
        let host = path("SPICE_BSIM330_HOST")?;
        let model = path("SPICE_BSIM330_MODEL")?;
        // Only process configuration can choose executable code. A deck
        // cannot set either path. Retain the module for the process lifetime.
        unsafe {
            let module = open_host(host.as_ptr());
            if module.is_null() {
                return Err("could not load BSIM3 native host".into());
            }
            let open = symbol(module, c"bsim330_open");
            let trial = symbol(module, c"bsim330_trial");
            if open.is_null() || trial.is_null() {
                return Err("BSIM3 host ABI unavailable".into());
            }
            let open: Open = std::mem::transmute(open);
            let code = open(model.as_ptr());
            if code != 0 {
                return Err(format!("BSIM3 module initialization failed ({code})"));
            }
            Ok(std::mem::transmute::<*mut c_void, Trial>(trial))
        }
    }

    pub(super) fn evaluate_fd(bias: [f64; 4]) -> Result<[f64; 4], String> {
        let trial = FD_TRIAL
            .get_or_init(|| {
                let host = path("SPICE_BSIM_SOI_FD_HOST")?;
                // The adapter retains its local legacy workspace. Hold one lock
                // across each evaluation, including the C error callback.
                unsafe {
                    let module = open_host(host.as_ptr());
                    if module.is_null() {
                        return Err("could not load BSIM3 SOI FD host".into());
                    }
                    let trial = symbol(module, c"spice_soi_fd_trial");
                    if trial.is_null() {
                        return Err("BSIM3 SOI FD host ABI unavailable".into());
                    }
                    Ok(std::sync::Mutex::new(std::mem::transmute::<
                        *mut c_void,
                        FdTrial,
                    >(trial)))
                }
            })
            .as_ref()
            .map_err(Clone::clone)?;
        let trial = trial
            .lock()
            .map_err(|_| "BSIM3 SOI FD host lock poisoned".to_owned())?;
        let mut output = [0.0; 4];
        let code = unsafe { trial(bias.as_ptr(), output.as_mut_ptr()) };
        if code != 0 || output.iter().any(|v| !v.is_finite()) {
            return Err(format!("BSIM3 SOI FD native trial failed ({code})"));
        }
        Ok(output)
    }

    pub(super) fn evaluate(p: [f64; 11], v: [f64; 4]) -> Result<[f64; 8], String> {
        let trial = TRIAL.get_or_init(bind).as_ref().map_err(Clone::clone)?;
        let mut output = [0.0; 8];
        // The checked host ABI consumes fixed-width arrays in D/G/S/B order.
        let code = unsafe { trial(p.as_ptr(), v.as_ptr(), output.as_mut_ptr()) };
        if code != 0 || output.iter().any(|v| !v.is_finite()) {
            return Err(format!("BSIM3 native trial failed ({code})"));
        }
        Ok(output)
    }
}

/// Returns terminal currents in amperes followed by terminal charges in coulombs.
pub fn evaluate(parameters: [f64; 11], bias: [f64; 4]) -> Result<[f64; 8], String> {
    #[cfg(all(any(windows, target_os = "linux"), target_arch = "x86_64"))]
    return native::evaluate(parameters, bias);
    #[cfg(not(all(any(windows, target_os = "linux"), target_arch = "x86_64")))]
    {
        let _ = (parameters, bias);
        Err("BSIM3 native host currently requires Windows x64 or Linux x86-64".into())
    }
}

/// Evaluates the trusted pinned SOI FD model's D/G/S/E terminal currents.
pub fn evaluate_fd(bias: [f64; 4]) -> Result<[f64; 4], String> {
    #[cfg(all(any(windows, target_os = "linux"), target_arch = "x86_64"))]
    return native::evaluate_fd(bias);
    #[cfg(not(all(any(windows, target_os = "linux"), target_arch = "x86_64")))]
    {
        let _ = bias;
        Err("BSIM3 SOI FD native host requires Windows x64 or Linux x86-64".into())
    }
}
