use crate::{
    compile::CompiledCircuit,
    error::{SimError, SimResult},
    quantity::positive,
};
use serde::{Deserialize, Serialize};
/// Local step-doubling tolerances, not a global waveform accuracy guarantee.
/// Current observers remain controlled as well as voltage/flux states.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct AdaptiveOptions {
    pub relative: f64,
    /// Optional current-observer relative budget; absent retains the historical
    /// shared relative tolerance. Voltage/flux units remain explicit below.
    #[serde(default,skip_serializing_if="Option::is_none")]
    pub current_relative: Option<f64>,
    pub voltage_absolute: f64,
    pub current_absolute: f64,
    pub min_step_s: f64,
    pub max_attempts: usize,
}

impl AdaptiveOptions {
    pub fn validate(&self, max_step: f64) -> SimResult<()> {
        positive(self.relative, "relative tolerance")?;
        if let Some(relative)=self.current_relative {
            positive(relative,"current relative tolerance")?;
            if relative>=1. { return Err(SimError::invalid("current relative tolerance must be below one")); }
        }
        positive(self.voltage_absolute, "voltage_absolute")?;
        positive(self.current_absolute, "current_absolute")?;
        positive(self.min_step_s, "min_step_s")?;
        if self.relative >= 1.
            || self.min_step_s > max_step
            || self.max_attempts == 0
            || self.max_attempts > 1_000_000
        {
            return Err(SimError::invalid("invalid adaptive bounds"));
        }
        Ok(())
    }
    pub(crate) fn error(
        &self,
        plan: &CompiledCircuit,
        coarse: &[f64],
        fine: &[f64],
        order: i32,
    ) -> SimResult<f64> {
        self.error_selected(plan,coarse,fine,order,1.)
    }
    /// A first-order restart also establishes endpoint derivative currents.
    /// Control their coarse/fine discrepancy once, preventing an initial
    /// derivative error from ringing through later trapezoidal intervals.
    pub(crate) fn restart_error(&self,plan:&CompiledCircuit,coarse:&[f64],fine:&[f64],order:i32)->SimResult<f64> {
        // Reserve three quarters of the current-error budget for subsequent
        // trapezoidal work: its derivative recurrence retains startup error.
        self.error_selected(plan,coarse,fine,order,0.25)
    }
    fn error_selected(&self,plan:&CompiledCircuit,coarse:&[f64],fine:&[f64],order:i32,current_budget:f64)->SimResult<f64> {
        if coarse.len()!=plan.unknown_count() || fine.len()!=plan.unknown_count() {
            return Err(SimError::invalid("adaptive state shape mismatch"));
        }
        let divisor = 2_f64.powi(order) - 1.;
        let mut norm = 0_f64;
        let mut worst = (0usize,0.,0.);
        for (i, (&a, &b)) in coarse.iter().zip(fine).enumerate() {
            if !a.is_finite() || !b.is_finite() { return Err(SimError::invalid("nonfinite adaptive state")); }
            let absolute = if i < plan.circuit.nodes.len() - 1 {
                self.voltage_absolute
            } else {
                self.current_absolute
            };
            let relative=if i<plan.circuit.nodes.len()-1 {self.relative} else {self.current_relative.unwrap_or(self.relative)};
            let scale = (absolute + relative * a.abs().max(b.abs())) *
                if i < plan.circuit.nodes.len()-1 {1.} else {current_budget};
            let value = (a - b).abs() / divisor / scale;
            if !scale.is_finite() || !value.is_finite() {
                return Err(SimError::invalid("adaptive error overflow"));
            }
            if value > norm { norm=value; worst=(i,a,b); }
        }
        if norm>1. {
            tracing::debug!(unknown=worst.0,coarse=worst.1,fine=worst.2,scaled_error=norm,current_budget,
                "adaptive_local_error_rejected");
        }
        Ok(norm)
    }
}

#[cfg(test)]
mod survivor_contracts {
    use super::*;
    #[test]
    fn finite_trials_cannot_hide_overflow_in_error_or_scale() {
        let plan=crate::compile::compile(&crate::netlist::parse("test\nR1 a 0 1\n.end").unwrap()).unwrap();
        let normal=AdaptiveOptions{relative:0.1,current_relative:None,voltage_absolute:1e-6,
            current_absolute:1e-9,min_step_s:1e-6,max_attempts:100};
        normal.validate(1e-3).unwrap();
        // Every operand is finite; subtraction alone overflows. A missing
        // computed-error check must not return Ok(infinity).
        assert_eq!(normal.error(&plan,&[f64::MAX],&[-f64::MAX],1).unwrap_err().code,
            crate::error::ErrorCode::InvalidInput);
        let huge=AdaptiveOptions{voltage_absolute:f64::MAX,..normal};
        huge.validate(1e-3).unwrap();
        // A finite discrepancy divided by an overflowed scale becomes zero.
        // That zero must never masquerade as a successful local error estimate.
        for value in [f64::MAX,f64::MAX/2.,f64::MAX/4.] {
            assert_eq!(huge.error(&plan,&[value],&[value/2.],1).unwrap_err().code,
                crate::error::ErrorCode::InvalidInput);
        }
    }
    #[test]
    fn rejection_event_records_only_errors_strictly_above_one() {
        use std::sync::{Arc,Mutex};
        use tracing_subscriber::{layer::{Layer,SubscriberExt},Registry};
        #[derive(Default)]
        struct Recorded { message:String, scaled:Option<f64>, level:Option<tracing::Level> }
        impl tracing::field::Visit for Recorded {
            fn record_debug(&mut self,field:&tracing::field::Field,value:&dyn std::fmt::Debug) {
                if field.name()=="message" {self.message=format!("{value:?}");}
            }
            fn record_f64(&mut self,field:&tracing::field::Field,value:f64) {
                if field.name()=="scaled_error" {self.scaled=Some(value);}
            }
        }
        struct Capture(Arc<Mutex<Vec<Recorded>>>);
        impl<S:tracing::Subscriber> Layer<S> for Capture {
            fn on_event(&self,event:&tracing::Event<'_>,_:tracing_subscriber::layer::Context<'_,S>) {
                let mut value=Recorded{level:Some(*event.metadata().level()),..Default::default()};
                event.record(&mut value);self.0.lock().unwrap().push(value);
            }
        }
        let plan=crate::compile::compile(&crate::netlist::parse("test\nR1 a 0 1\n.end").unwrap()).unwrap();
        let options=AdaptiveOptions{relative:0.5,current_relative:None,voltage_absolute:0.5,
            current_absolute:0.01,min_step_s:1e-6,max_attempts:100};
        let records=Arc::new(Mutex::new(Vec::new()));
        // Thread-local dispatch avoids changing another test's global logging.
        tracing::subscriber::with_default(Registry::default().with(Capture(records.clone())),|| {
            for (fine,expected) in [(0.,0.),(1.,1.),(2.,4./3.)] {
                assert!((options.error(&plan,&[0.],&[fine],1).unwrap()-expected).abs()<1e-14);
            }
        });
        let records=records.lock().unwrap();
        let rejected:Vec<_>=records.iter().filter(|e|e.message.contains("adaptive_local_error_rejected")).collect();
        assert_eq!(rejected.len(),1,"accepted states must not be logged as rejected");
        assert_eq!(rejected[0].level,Some(tracing::Level::DEBUG));
        assert!((rejected[0].scaled.unwrap()-4./3.).abs()<1e-14);
    }
    #[test]
    fn voltage_and_branch_errors_keep_their_distinct_si_budgets() {
        let plan=crate::compile::compile(&crate::netlist::parse("test\nV1 a 0 1\nR1 a 0 1\n.end").unwrap()).unwrap();
        let options=AdaptiveOptions{relative:0.1,current_relative:Some(0.2),voltage_absolute:0.2,
            current_absolute:0.01,min_step_s:1e-6,max_attempts:100};
        // Independently derived step-doubling errors: 0.5/(0.2+0.1*1)
        // for a node voltage and 0.5/(0.01+0.2*1) for a branch current.
        assert!((options.error(&plan,&[1.,1.],&[0.5,1.],1).unwrap()-5./3.).abs()<1e-14);
        assert!((options.restart_error(&plan,&[1.,1.],&[0.5,1.],1).unwrap()-5./3.).abs()<1e-14);
        assert!((options.error(&plan,&[1.,1.],&[1.,0.5],1).unwrap()-50./21.).abs()<1e-14);
    }
    #[test]
    fn independently_nonfinite_trials_are_rejected_by_the_error_boundary() {
        let plan=crate::compile::compile(&crate::netlist::parse("test\nR1 a 0 1\n.end").unwrap()).unwrap();
        let options=AdaptiveOptions{relative:0.1,current_relative:None,voltage_absolute:1e-6,
            current_absolute:1e-9,min_step_s:1e-6,max_attempts:100};
        for bad in [f64::NAN,f64::INFINITY,f64::NEG_INFINITY] {
            for (coarse,fine) in [(bad,1.),(1.,bad),(bad,bad)] {
                for order in [1,2] {
                    let error=options.error_selected(&plan,&[coarse],&[fine],order,1.).unwrap_err();
                    assert_eq!(error.code,crate::error::ErrorCode::InvalidInput);
                }
            }
        }
        // Even if the early finite-state guard is changed, subtraction or
        // scaling remains nonfinite and the later guard rejects InvalidInput.
        // Diagnostic text is contextual, not a stable error identifier.
    }
    #[test]
    fn algebraic_current_accuracy_and_restart_budget_remain_explicit() {
        let plan=crate::compile::compile(&crate::netlist::parse("V1 a 0 1\nR1 a 0 1").unwrap()).unwrap();
        let options=AdaptiveOptions { current_relative: None,relative:0.1,voltage_absolute:0.1,current_absolute:0.01,min_step_s:1e-6,max_attempts:100};
        assert!((options.error(&plan,&[1.,1.],&[1.,0.5],1).unwrap()-50./11.).abs()<1e-14);
        assert!((options.restart_error(&plan,&[1.,1.],&[1.,0.5],1).unwrap()-200./11.).abs()<1e-14);
        assert!(options.error(&plan,&[1.,1.],&[1.,f64::NAN],1).is_err());
        assert!(options.error(&plan,&[1.],&[1.,0.5],1).is_err());
    }
    #[test]
    fn current_relative_is_optional_and_has_its_own_validated_units() {
        let mut options:AdaptiveOptions=serde_json::from_str(r#"{"relative":0.001,"voltage_absolute":0.1,"current_absolute":0.01,"min_step_s":0.000001,"max_attempts":100}"#).unwrap();
        assert!(options.current_relative.is_none());
        for bad in [0.,-0.1,1.,f64::INFINITY,f64::NAN] {
            options.current_relative=Some(bad);assert!(options.validate(0.001).is_err());
        }
        options.current_relative=Some(0.1);options.validate(0.001).unwrap();
        let plan=crate::compile::compile(&crate::netlist::parse("V1 a 0 1\nR1 a 0 1").unwrap()).unwrap();
        assert!((options.error(&plan,&[1.,1.],&[1.,0.5],1).unwrap()-50./11.).abs()<1e-14);
        let roundtrip=serde_json::to_string(&options).unwrap();
        assert!(roundtrip.contains("current_relative"));
    }
    #[test]
    fn trapezoidal_step_doubling_uses_the_second_order_richardson_divisor() {
        let plan = crate::compile::compile(
            &crate::netlist::parse("title\nV1 in 0 1\nR1 in out 1\nC1 out 0 1").unwrap(),
        )
        .unwrap();
        let options = AdaptiveOptions { current_relative: None,
            relative: 0.1,
            voltage_absolute: 0.1,
            current_absolute: 0.01,
            min_step_s: 1e-6,
            max_attempts: 100,
        };
        let coarse = [0., 0., 0.];
        let fine = [0., 0.9, 0.];
        assert!((options.error(&plan, &coarse, &fine, 2).unwrap() - 30. / 19.).abs() < 1e-12);
        assert!((options.error(&plan, &coarse, &fine, 1).unwrap() - 90. / 19.).abs() < 1e-12);
    }

    #[test]
    fn first_flux_branch_error_uses_the_current_absolute_tolerance() {
        let plan =
            crate::compile::compile(&crate::netlist::parse("L1 a 0 1\nR1 a 0 1").unwrap())
                .unwrap();
        let options = AdaptiveOptions { current_relative: None,
            relative: 0.1,
            voltage_absolute: 0.1,
            current_absolute: 0.01,
            min_step_s: 1e-6,
            max_attempts: 100,
        };
        // First unknown is a voltage; the next is the inductor flux current.
        // BE: delta=0.5 A, current scale=0.01+0.1*1=0.11 A, ratio=50/11.
        let ratio = options.error(&plan, &[0., 1.], &[0., 0.5], 1).unwrap();
        assert!((ratio - 50. / 11.).abs() < 1e-14);
    }
}
