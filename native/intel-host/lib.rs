//! Trusted optional GPU ABI loader. Unsafe operations remain outside the core.
#[cfg(windows)]
mod native {
    use std::{
        ffi::{CStr, c_void},
        path::PathBuf,
        sync::OnceLock,
    };
    type Solve = unsafe extern "C" fn(u32, *const f64, *const f64, *mut f64) -> i32;
    type Info = unsafe extern "C" fn(*mut u8, u32) -> i32;
    type Error = unsafe extern "C" fn() -> *const std::ffi::c_char;
    type Close = unsafe extern "C" fn() -> i32;
    struct Provider {
        solve: Solve,
        info: Info,
        error: Error,
        close: Close,
    }
    static PROVIDER: OnceLock<Result<Provider, String>> = OnceLock::new();
    #[link(name = "kernel32")]
    unsafe extern "system" {
        fn LoadLibraryExW(path: *const u16, file: *mut c_void, flags: u32) -> *mut c_void;
        fn GetProcAddress(module: *mut c_void, name: *const u8) -> *mut c_void;
    }
    fn bind() -> Result<&'static Provider, String> {
        PROVIDER
            .get_or_init(|| {
                let path = std::env::var_os("SPICE_INTEL_HOST")
                    .map(PathBuf::from)
                    .filter(|p| p.is_absolute() && p.is_file())
                    .ok_or(
                        "SPICE_INTEL_HOST must select an absolute trusted native provider file",
                    )?;
                use std::os::windows::ffi::OsStrExt;
                let wide: Vec<_> = path.as_os_str().encode_wide().chain([0]).collect();
                // Retain the module for process lifetime. Safe Windows search flags
                // resolve dependencies beside the module and in system directories.
                unsafe {
                    let module = LoadLibraryExW(wide.as_ptr(), std::ptr::null_mut(), 0x900);
                    if module.is_null() {
                        return Err("Could not load Intel native provider".into());
                    }
                    let symbol = |name: &CStr| {
                        let p = GetProcAddress(module, name.as_ptr().cast());
                        if p.is_null() {
                            Err(format!("Missing Intel ABI symbol {name:?}"))
                        } else {
                            Ok(p)
                        }
                    };
                    let abi: unsafe extern "C" fn() -> u32 =
                        std::mem::transmute(symbol(c"spice_intel_abi")?);
                    if abi() != 1 {
                        return Err("Unsupported Intel provider ABI".into());
                    }
                    Ok(Provider {
                        solve: std::mem::transmute::<*mut c_void, Solve>(symbol(
                            c"spice_intel_solve",
                        )?),
                        info: std::mem::transmute::<*mut c_void, Info>(symbol(
                            c"spice_intel_info",
                        )?),
                        error: std::mem::transmute::<*mut c_void, Error>(symbol(
                            c"spice_intel_error",
                        )?),
                        close: std::mem::transmute::<*mut c_void, Close>(symbol(
                            c"spice_intel_close",
                        )?),
                    })
                }
            })
            .as_ref()
            .map_err(Clone::clone)
    }
    fn failure(p: &Provider) -> String {
        // ABI contract: nonnull NUL-terminated thread-local message on failure.
        unsafe {
            let msg = (p.error)();
            if msg.is_null() {
                "Intel provider returned no error text".into()
            } else {
                CStr::from_ptr(msg).to_string_lossy().into_owned()
            }
        }
    }
    pub fn info() -> Result<String, String> {
        let p = bind()?;
        let mut text = [0u8; 4096];
        if unsafe { (p.info)(text.as_mut_ptr(), text.len() as u32) } != 0 {
            return Err(failure(p));
        }
        CStr::from_bytes_until_nul(&text)
            .map(|s| s.to_string_lossy().into_owned())
            .map_err(|e| e.to_string())
    }
    /// Releases an initialized context outside DLL process-detach. It neither
    /// loads an unused provider nor invalidates the trusted module binding.
    pub fn shutdown() -> Result<(), String> {
        if let Some(Ok(p)) = PROVIDER.get() {
            if unsafe { (p.close)() } != 0 {
                return Err(failure(p));
            }
        }
        Ok(())
    }
    pub fn solve(n: usize, a: &[f64], b: &[f64]) -> Result<Vec<f64>, String> {
        if n == 0
            || n > 512
            || a.len() != n * n
            || b.len() != n
            || a.iter().chain(b).any(|v| !v.is_finite())
        {
            return Err("Invalid bounded GPU linear system".into());
        }
        let p = bind()?;
        let mut out = vec![f64::NAN; n];
        // All slice lengths and the bounded dimension are checked before FFI.
        if unsafe { (p.solve)(n as u32, a.as_ptr(), b.as_ptr(), out.as_mut_ptr()) } != 0 {
            return Err(failure(p));
        }
        if out.iter().any(|v| !v.is_finite()) {
            return Err("Intel provider returned nonfinite data".into());
        }
        Ok(out)
    }
}
#[cfg(windows)]
pub use native::{info, shutdown, solve};
#[cfg(not(windows))]
pub fn shutdown() -> Result<(), String> {
    Ok(())
}
#[cfg(not(windows))]
pub fn info() -> Result<String, String> {
    Err("Intel provider is currently qualified only on Windows".into())
}
#[cfg(not(windows))]
pub fn solve(_: usize, _: &[f64], _: &[f64]) -> Result<Vec<f64>, String> {
    Err("Intel provider is currently qualified only on Windows".into())
}
