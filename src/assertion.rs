use serde::Serialize;
/// Missing/invalid measurements cannot become passing assertions.
#[derive(Debug,PartialEq,Eq,Serialize)]
#[serde(rename_all="snake_case")]
pub enum Outcome{Pass,Fail,Indeterminate}
pub fn within(value:Option<f64>,low:f64,high:f64)->Outcome{
    if !low.is_finite()||!high.is_finite()||low>high{return Outcome::Indeterminate;}
    match value{Some(v)if v.is_finite()=>if v>=low&&v<=high{Outcome::Pass}else{Outcome::Fail},_=>Outcome::Indeterminate}
}

