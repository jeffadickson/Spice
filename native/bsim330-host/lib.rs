//! Isolates the opt-in Windows native model ABI from the safe solver crate.
#[cfg(all(windows, target_arch = "x86_64"))]
mod windows {
    use std::{ffi::c_void, os::windows::ffi::OsStrExt, path::PathBuf, sync::OnceLock};

    type Open = unsafe extern "C" fn(*const u16) -> i32;
    type Trial = unsafe extern "C" fn(*const f64, *const f64, *mut f64) -> i32;
    static TRIAL: OnceLock<Result<Trial, String>> = OnceLock::new();

    #[link(name = "kernel32")]
    unsafe extern "system" {
        fn LoadLibraryExW(path: *const u16, file: *mut c_void, flags: u32) -> *mut c_void;
        fn GetProcAddress(library: *mut c_void, name: *const u8) -> *mut c_void;
    }

    fn path(name: &str) -> Result<Vec<u16>, String> {
        let path = std::env::var_os(name)
            .map(PathBuf::from)
            .filter(|p| p.is_absolute() && p.is_file())
            .ok_or_else(|| format!("{name} must select an absolute trusted native file"))?;
        Ok(path.as_os_str().encode_wide().chain([0]).collect())
    }

    fn bind() -> Result<Trial, String> {
        let host = path("SPICE_BSIM330_HOST")?;
        let model = path("SPICE_BSIM330_MODEL")?;
        // Only process configuration can choose executable code. A deck
        // cannot set either path. Retain the module for the process lifetime.
        unsafe {
            let module = LoadLibraryExW(host.as_ptr(), std::ptr::null_mut(), 0x800);
            if module.is_null() {
                return Err("could not load BSIM3 native host".into());
            }
            let open = GetProcAddress(module, c"bsim330_open".as_ptr().cast());
            let trial = GetProcAddress(module, c"bsim330_trial".as_ptr().cast());
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
    #[cfg(all(windows, target_arch = "x86_64"))]
    return windows::evaluate(parameters, bias);
    #[cfg(not(all(windows, target_arch = "x86_64")))]
    {
        let _ = (parameters, bias);
        Err("BSIM3 native host currently requires Windows x64".into())
    }
}
