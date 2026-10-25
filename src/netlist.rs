use crate::{circuit::{Circuit,Element},error::{ErrorCode,SimError,SimResult},ids::NodeId,quantity::spice_scalar};
/// Explicit small subset: optional title, R/C/V/I, .op and .end. No includes or control commands.
pub fn parse(text:&str)->SimResult<Circuit>{
    if text.len()>1_048_576{return Err(SimError::new(ErrorCode::ResourceExhausted,"netlist exceeds 1 MiB"));}
    let mut circuit=Circuit{nodes:vec!["0".into()],elements:Vec::new()};
    let mut ended=false;
    for(index,line)in text.lines().enumerate(){
        let line=line.trim();
        if line.is_empty()||line.starts_with('*'){continue;}
        let tokens:Vec<&str>=line.split_whitespace().collect();
        let name=tokens[0];
        if ended{return Err(SimError::invalid(format!("line {}: content after .end",index+1)));}
        if name.eq_ignore_ascii_case(".end"){if tokens.len()!=1{return Err(SimError::invalid("invalid .end"));}ended=true;continue;}
        if name.eq_ignore_ascii_case(".op")&&tokens.len()==1{continue;}
        let kind=name.chars().next().unwrap().to_ascii_uppercase();
        if index==0&&tokens.len()!=4&&!name.starts_with('.') {continue;}
        if !matches!(kind,'R'|'C'|'V'|'I'){return Err(SimError::new(ErrorCode::Unsupported,format!("line {}: unsupported element/directive",index+1)));}
        if tokens.len()!=4{return Err(SimError::invalid(format!("line {}: expected id p n value",index+1)));}
        let mut node=|label:&str|->NodeId{
            let label=label.to_ascii_lowercase();
            if let Some(i)=circuit.nodes.iter().position(|v|v==&label){NodeId(i)}else{circuit.nodes.push(label);NodeId(circuit.nodes.len()-1)}
        };
        let p=node(tokens[1]);let n=node(tokens[2]);
        let value=spice_scalar(tokens[3]).map_err(|e|SimError::invalid(format!("line {}: {}",index+1,e.message)))?;
        let id=name.to_ascii_uppercase();
        circuit.elements.push(match kind{
            'R'=>Element::Resistor{id,p,n,ohms:value},
            'C'=>Element::Capacitor{id,p,n,farads:value},
            'V'=>Element::Voltage{id,p,n,volts:value},
            'I'=>Element::Current{id,p,n,amps:value},
            _=>unreachable!()
        });
        if circuit.elements.len()>1024||circuit.nodes.len()>128{return Err(SimError::new(ErrorCode::ResourceExhausted,"netlist circuit limit exceeded"));}
    }
    circuit.validate()?;Ok(circuit)
}

