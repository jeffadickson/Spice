// Owned C ABI glue. Numerical GMRES and OpenCL kernels are ViennaCL 1.7.1;
// this module never calls a CPU linear solver or selects a CPU OpenCL device.
#define VIENNACL_WITH_OPENCL
#include <viennacl/compressed_matrix.hpp>
#include <viennacl/linalg/gmres.hpp>
#include <viennacl/linalg/row_scaling.hpp>
#include <viennacl/ocl/backend.hpp>
#include <CL/cl_ext.h>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <map>
#include <mutex>
#include <sstream>
#include <stdexcept>
#include <vector>
#include <cstdio>
#include <cstdlib>

#ifdef _WIN32
#define EXPORT extern "C" __declspec(dllexport)
#else
#define EXPORT extern "C" __attribute__((visibility("default")))
#endif

namespace {
std::mutex gate;
std::string device_identity;
bool context_ready=false;
thread_local std::string last_error;
// Opt-in native lifecycle tracing goes only to stderr, never protocol stdout.
void phase(const char* label) {if(std::getenv("SPICE_INTEL_TRACE")){std::fprintf(stderr,"intel_opencl phase=%s\n",label);std::fflush(stderr);}}

std::string text(cl_device_id device, cl_device_info key) {
    size_t size=0;
    if(clGetDeviceInfo(device,key,0,nullptr,&size)!=CL_SUCCESS || size==0 || size>65536)
        throw std::runtime_error("OpenCL device metadata unavailable");
    std::vector<char> bytes(size);
    if(clGetDeviceInfo(device,key,size,bytes.data(),nullptr)!=CL_SUCCESS)
        throw std::runtime_error("OpenCL device metadata read failed");
    return std::string(bytes.data());
}

void initialize(bool compute=true) {
    if((compute && context_ready)||(!compute && !device_identity.empty())) return;
    cl_uint count=0;
    if(clGetPlatformIDs(0,nullptr,&count)!=CL_SUCCESS || count==0 || count>128)
        throw std::runtime_error("No OpenCL platforms");
    std::vector<cl_platform_id> platforms(count);
    if(clGetPlatformIDs(count,platforms.data(),nullptr)!=CL_SUCCESS)
        throw std::runtime_error("OpenCL platform enumeration failed");
    for(auto platform:platforms) {
        cl_uint n=0;
        auto status=clGetDeviceIDs(platform,CL_DEVICE_TYPE_GPU,0,nullptr,&n);
        if(status==CL_DEVICE_NOT_FOUND) continue;
        if(status!=CL_SUCCESS || n>128) throw std::runtime_error("OpenCL GPU enumeration failed");
        std::vector<cl_device_id> devices(n);
        if(clGetDeviceIDs(platform,CL_DEVICE_TYPE_GPU,n,devices.data(),nullptr)!=CL_SUCCESS)
            throw std::runtime_error("OpenCL GPU list failed");
        for(auto device:devices) {
            cl_device_fp_config fp64=0;
            if(text(device,CL_DEVICE_VENDOR).find("Intel")==std::string::npos) continue;
            if(clGetDeviceInfo(device,CL_DEVICE_DOUBLE_FP_CONFIG,sizeof(fp64),&fp64,nullptr)!=CL_SUCCESS || !fp64) continue;
            // Pin one explicit GPU; ViennaCL's default-device selection is not used.
            if(compute) {
                phase("context_create");
                viennacl::ocl::context fresh;
                fresh.add_device(device);fresh.init();fresh.add_queue(viennacl::ocl::device(device));
                viennacl::ocl::backend<>::add_context(0,fresh);
                context_ready=true;
                viennacl::ocl::switch_context(0);
            }
            auto identity=text(device,CL_DEVICE_NAME)+" | driver "+text(device,CL_DRIVER_VERSION)+" | ViennaCL 1.7.1 OpenCL FP64 GMRES";
            if(compute) viennacl::ocl::current_context().get_queue().finish();
            device_identity=identity;
            phase(compute?"context_ready":"device_enumerated_without_context");
            return;
        }
    }
    throw std::runtime_error("No Intel GPU with native OpenCL FP64; CPU fallback forbidden");
}

// Componentwise backward error uses the ORIGINAL physical system, not the
// equilibrated system or ViennaCL's normwise iteration estimate.
void check_residual(unsigned n,const double* a,const double* b,const double* x, double tolerance=1e-10) {
    for(unsigned i=0;i<n;++i) {
        double residual=-b[i],scale=std::abs(b[i]);
        for(unsigned j=0;j<n;++j) {
            const double product=a[i*n+j]*x[j];
            residual+=product;
            scale+=std::abs(product);
        }
        const double error=scale==0 ? std::abs(residual) : std::abs(residual)/scale;
        if(!std::isfinite(residual) || !std::isfinite(scale) || !std::isfinite(error) || error>tolerance) {
            std::ostringstream detail; detail.precision(17);
            detail<<"GPU original backward-error gate: row="<<i<<" error="<<error<<" residual="<<residual<<" scale="<<scale<<" solution=";
            for(unsigned j=0;j<n;++j) detail<<x[j]<<",";
            throw std::runtime_error(detail.str());
        }
    }
}
}

// Stable ABI v1. Error text belongs to the calling thread until its next call.
EXPORT const char* spice_intel_error() { return last_error.c_str(); }
EXPORT unsigned spice_intel_abi() { return 1; }
// Release OpenCL handles during ordinary execution, before Windows enters DLL
// process-detach under its loader lock. Driver calls from static destructors can
// otherwise deadlock process shutdown, including metadata-only discovery.
EXPORT int spice_intel_close() {
    try {
        phase("close_lock");
        std::lock_guard<std::mutex> lock(gate);
        phase("close_locked");
        if(context_ready) {
            phase("close_finish");
            viennacl::ocl::current_context().get_queue().finish();
            phase("close_replace");
            viennacl::ocl::backend<>::release_context(0);
            context_ready=false;
        }
        phase("close_done");
        return 0;
    } catch(const std::exception& e) {last_error=e.what();return 1;}
      catch(...) {last_error="Intel context shutdown failure";return 1;}
}
EXPORT int spice_intel_info(char* output,unsigned capacity) {
    try {
        std::lock_guard<std::mutex> lock(gate); initialize(false);
        if(!output || capacity<=device_identity.size()) throw std::runtime_error("Device info buffer too small");
        std::memcpy(output,device_identity.c_str(),device_identity.size()+1); return 0;
    } catch(const std::exception& e) { last_error=e.what(); return 1; }
      catch(...) { last_error="Unknown native Intel initialization failure"; return 1; }
}

EXPORT int spice_intel_solve(unsigned n,const double* a,const double* b,double* output) {
    try {
        std::lock_guard<std::mutex> lock(gate);
        if(n==0 || n>512 || !a || !b || !output) throw std::runtime_error("Invalid bounded GPU solve dimensions");
        for(unsigned i=0;i<n*n;++i) if(!std::isfinite(a[i])) throw std::runtime_error("Nonfinite GPU matrix");
        for(unsigned i=0;i<n;++i) if(!std::isfinite(b[i])) throw std::runtime_error("Nonfinite GPU RHS");
        initialize();
        std::vector<double> row_scale(n),column_scale(n,0),rhs(n),result(n);
        std::vector<std::map<unsigned,double>> entries(n);
        for(unsigned i=0;i<n;++i) {
            double norm=0; for(unsigned j=0;j<n;++j) norm=std::max(norm,std::abs(a[i*n+j]));
            if(norm==0) throw std::runtime_error("Zero matrix row");
            row_scale[i]=norm;
            for(unsigned j=0;j<n;++j) column_scale[j]=std::max(column_scale[j],std::abs(a[i*n+j]/norm));
        }
        for(unsigned j=0;j<n;++j) if(column_scale[j]==0) throw std::runtime_error("Zero matrix column");
        for(unsigned i=0;i<n;++i) {
            rhs[i]=b[i]/row_scale[i];
            if(!std::isfinite(rhs[i]) || (b[i]!=0 && rhs[i]==0)) throw std::runtime_error("GPU RHS equilibration overflow/underflow");
            for(unsigned j=0;j<n;++j) if(a[i*n+j]!=0) {
                double value=(a[i*n+j]/row_scale[i])/column_scale[j];
                if(!std::isfinite(value) || value==0) throw std::runtime_error("GPU equilibration overflow/underflow");
                entries[i][j]=value;
            }
        }
        const auto context=viennacl::context(viennacl::ocl::current_context());
        viennacl::compressed_matrix<double> matrix(n,n,context);
        viennacl::vector<double> load(n,context),solution(n,context);
        viennacl::copy(entries,matrix); viennacl::copy(rhs,load);
        // General nonsymmetric solve: zero source-constraint diagonals are valid.
        viennacl::linalg::gmres_tag tag(1e-13,1024,std::min(n,128u));
        // Use the library's general preconditioned GMRES path. Row-norm
        // preconditioning is valid for zero diagonal source constraints.
        viennacl::linalg::row_scaling<viennacl::compressed_matrix<double>> preconditioner(matrix,viennacl::linalg::row_scaling_tag(2));
        solution=viennacl::linalg::solve(matrix,load,tag,preconditioner);
        viennacl::copy(solution,result);
        for(unsigned i=0;i<n;++i) {
            result[i]/=column_scale[i];
            if(!std::isfinite(result[i])) throw std::runtime_error("Nonfinite GPU solution");
        }
        // Normwise GMRES convergence is insufficient for mixed-unit MNA rows.
        // Keep the unchanged physical gate and allow three GPU-solved residual
        // corrections, never a fallback CPU factorization.
        for(unsigned correction=0;;++correction) {
            // An exact homogeneous singleton row is an exact zero constraint.
            // Restore it without discarding any nonzero matrix coefficient;
            // every coupled row is still checked after this projection.
            for(unsigned i=0;i<n;++i) if(b[i]==0) {
                unsigned count=0,column=0;
                for(unsigned j=0;j<n;++j) if(a[i*n+j]!=0) {++count;column=j;}
                if(count==1) result[column]=0;
            }
            // Leave margin below the public acceptance gate so repeated circuit
            // solves and well-conditioned forward-error checks remain useful.
            try {check_residual(n,a,b,result.data(),1e-12);break;}
            catch(const std::runtime_error&) {if(correction==3) throw;}
            phase("gpu_residual_refinement");
            for(unsigned i=0;i<n;++i) {
                double residual=b[i];
                for(unsigned j=0;j<n;++j) residual=std::fma(-a[i*n+j],result[j],residual);
                rhs[i]=residual/row_scale[i];
                if(!std::isfinite(rhs[i]) || (residual!=0 && rhs[i]==0))
                    throw std::runtime_error("GPU residual scaling overflow/underflow");
            }
            viennacl::copy(rhs,load);
            solution=viennacl::linalg::solve(matrix,load,tag,preconditioner);
            std::vector<double> delta(n);viennacl::copy(solution,delta);
            for(unsigned i=0;i<n;++i) {
                result[i]+=delta[i]/column_scale[i];
                if(!std::isfinite(result[i])) throw std::runtime_error("Nonfinite GPU refined solution");
            }
        }
        phase("solve_verified");
        std::copy(result.begin(),result.end(),output);
        return 0;
    } catch(const std::exception& e) { last_error=e.what(); return 1; }
      catch(...) { last_error="Unknown native Intel solve failure"; return 1; }
}
