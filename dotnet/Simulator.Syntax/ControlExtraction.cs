using System.Collections.Immutable;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Simulator.Syntax.Generated;
namespace Simulator.Syntax;
// Generated command tokens remain the lexical authority. No Split/regex scanner
// interprets commands; default deck lexing remains unchanged outside this entry.
internal static class ControlExtraction
{
 private static bool SimpleName(string value)=>value.Length is >0 and <=64
     &&(char.IsAsciiLetter(value[0])||value[0]=='_')&&value.All(c=>char.IsAsciiLetterOrDigit(c)||c=='_');

 internal static ControlBlockSyntax Extract(SpiceParser.ControlContext control,int scope,Func<int,int,SourceSyntax> capture,Func<ParserRuleContext,SourceRange> range){
  SourceRange Token(IToken token)=>capture(token.StartIndex,token.StopIndex).Range;
  var lines=control.COMMAND_LINE();if(lines.Length>256)return new(scope,range(control),Token(control.CONTROL().Symbol),Token(control.ENDC().Symbol),[],"control_command_budget");
  var commands=lines.Select(line=>Command(line.Symbol)).ToImmutableArray();return new(scope,range(control),Token(control.CONTROL().Symbol),Token(control.ENDC().Symbol),commands);
  ControlCommandSyntax Command(IToken token){
   var physical=Token(token);if(token.Text.Length>4096)return new("","unsupported",null,[],physical,"control_command_length_budget");
   var lexer=new SpiceLexer(new AntlrInputStream(token.Text)){ControlSemantic=true};lexer.Mode(SpiceLexer.BODY);lexer.RemoveErrorListeners();var tokens=new CommonTokenStream(lexer);tokens.Fill();
   if(tokens.Size>256)return new("","unsupported",null,[],physical,"control_token_budget");
   var first=tokens.LT(1).Text;
   var echo=first.Equals("echo",StringComparison.OrdinalIgnoreCase);
   var print=first.Equals("print",StringComparison.OrdinalIgnoreCase);
   var loop=first.Equals("foreach",StringComparison.OrdinalIgnoreCase);
   var letCommand=first.Equals("let",StringComparison.OrdinalIgnoreCase);
   var visible=tokens.GetTokens().Where(t=>t.Channel==TokenConstants.DefaultChannel).ToArray();
   bool IsLetNodeVariable(IToken token){
    var index=Array.IndexOf(visible,token);
    return index>=2&&index+1<visible.Length&&visible[index-2].Text.Equals("v",StringComparison.OrdinalIgnoreCase)
        &&visible[index-1].Type==SpiceLexer.LPAREN&&visible[index+1].Type==SpiceLexer.RPAREN
        &&token.Text.StartsWith('$')&&SimpleName(token.Text[1..]);
   }
   var unsafeToken=tokens.GetTokens().FirstOrDefault(t=>t.Type is SpiceLexer.CONTROL_SEPARATOR or SpiceLexer.INVALID or SpiceLexer.SLASH_COMMENT ||
       (echo||print)&&t.Text is ("<" or ">>" or "|" or "&") || echo&&t.Text==">" ||
       t.Type==SpiceLexer.CONTROL_VARIABLE&&(!echo&&!first.Equals("if",StringComparison.OrdinalIgnoreCase)&&!loop&&!(letCommand&&IsLetNodeVariable(t))||!t.Text.StartsWith('$')));
   if(unsafeToken!=null)return new("","unsupported",null,[],physical,"unsafe_control_separator_variable_or_token");
   tokens.Seek(0);var parser=new SpiceParser(tokens){ErrorHandler=new BailErrorStrategy()};parser.RemoveErrorListeners();
   SourceSyntax Capture(ParserRuleContext context)=>capture(token.StartIndex+context.Start.StartIndex,token.StartIndex+context.Stop.StopIndex);
   try{
    var tree=parser.controlCommandProfile();var item=tree.GetChild(0) as ParserRuleContext;
    if(item==null)return new("","comment",null,[],physical);
    AnalysisSyntax? analysis=null;var name=item.Start.Text.ToLowerInvariant();var kind="unsupported";ImmutableArray<ControlSettingSyntax> settings=[];ControlPostprocessingSyntax? postprocessing=null;string? redirect=null;string? argument=null;ControlForeachSyntax? foreachHeader=null;
    switch(item){
     case SpiceParser.ControlOpContext:kind="analysis";analysis=new(scope,"op",[],[],physical);break;
     case SpiceParser.ControlAcContext ac:kind="analysis";analysis=new(scope,"ac",[ac.identifier(1).GetText()],ac.controlNumber().Select(Capture).ToImmutableArray(),physical);break;
     case SpiceParser.ControlDcContext dc:kind="analysis";analysis=new(scope,"dc",dc.identifier().Skip(1).Select(i=>i.GetText()).ToImmutableArray(),dc.controlNumber().Select(Capture).ToImmutableArray(),physical);break;
     case SpiceParser.ControlTranContext tran:kind="analysis";analysis=new(scope,"tran",tran.identifier().Skip(1).Select(i=>i.GetText()).ToImmutableArray(),tran.controlNumber().Select(Capture).ToImmutableArray(),physical);break;
     case SpiceParser.ControlRunContext:kind="run";break;
     case SpiceParser.ControlIfContext:kind="if";break;
     case SpiceParser.ControlForeachContext each:
      var variable=each.identifier(1).GetText();
      var vector=each.CONTROL_VARIABLE().GetText();
      if(SimpleName(variable)&&vector.StartsWith("$&",StringComparison.Ordinal)&&SimpleName(vector[2..])){
       kind="foreach";foreachHeader=new(variable,vector[2..]);
      }
      break;
     case SpiceParser.ControlBoundaryContext:kind=name;break;
     case SpiceParser.ControlEchoContext:kind=token.Text.Contains('$')?"report_substitution":"report";break;
     case SpiceParser.ControlQuitContext exit:
      kind=exit.NUMBER()?.GetText() switch {null=>"quit","0" or "1"=>"quit_status",_=>"unsupported"};break;
     case SpiceParser.ControlRedirectContext capturePrint:
      redirect=capturePrint.identifier(2).GetText();argument=capturePrint.identifier(1).GetText();
      kind=ControlReportTarget.IsSafe(redirect)?"report_redirect":"unsupported";break;
     case SpiceParser.ControlPowerVectorTypeContext vectorType:
      const string powerSuffix=":power";
      var vectorName=vectorType.identifier(2).GetText();
      if(vectorType.identifier(1).GetText().Equals("power",StringComparison.OrdinalIgnoreCase)&&
          vectorName.EndsWith(powerSuffix,StringComparison.OrdinalIgnoreCase)&&SimpleName(vectorName[..^powerSuffix.Length]))
       kind="power_vector_type";
      break;
     case SpiceParser.ControlReportContext:kind=print&&tokens.GetTokens().Any(t=>t.Text==">")?"unsupported":"report";break;
     case SpiceParser.ControlLetContext let:
      kind="postprocessing";postprocessing=new(ControlPostprocessingKind.Let,[let.identifier(1).GetText()],Capture(let.expression()));break;
     case SpiceParser.ControlDefineContext define:
      kind="postprocessing";postprocessing=new(ControlPostprocessingKind.Define,define.identifier().Skip(1).Select(i=>i.GetText()).ToImmutableArray(),Capture(define.expression()));break;
     case SpiceParser.ControlUnletContext unlet:
      kind="postprocessing";postprocessing=new(ControlPostprocessingKind.Unlet,unlet.identifier().Skip(1).Select(i=>i.GetText()).ToImmutableArray(),null);break;
     case SpiceParser.ControlSetContext set:
      kind="set";settings=set.parameter().Select(p=>{
       if(p.value() is {} flagValue&&flagValue.PLUS()==null&&flagValue.MINUS()==null&&flagValue.identifier() is {} flag)return new ControlSettingSyntax(flag.GetText(),null,null,false,Capture(p).Range);
       if(p.assignment() is not {} assignment)return new(null,null,null,false,Capture(p).Range);
       var value=assignment.GetChild(2) as ParserRuleContext;var scalar=value as SpiceParser.ValueContext;
       // Reporting allowlists apply to exact unsigned literals. A value context
       // may retain an identifier/number beneath a unary sign; do not erase it.
       var unsigned=scalar?.PLUS()==null&&scalar?.MINUS()==null;
       return new ControlSettingSyntax(assignment.referenceName()?.GetText(),value==null?null:Capture(value),unsigned?scalar?.identifier()?.GetText():null,unsigned&&scalar?.NUMBER()!=null,Capture(p).Range);
      }).ToImmutableArray();
      if(settings.Length==1 && settings[0] is {Name: {} local} setting &&
         (local.StartsWith("local_",StringComparison.OrdinalIgnoreCase)||local.StartsWith("testvar_",StringComparison.OrdinalIgnoreCase)) &&
         local.Length<=64 && local.All(c=>char.IsAsciiLetterOrDigit(c)||c=='_') &&
         (setting.Value==null||setting.Value.Text=="\"\"")) kind="local_set";
      if(settings.Length==1&&settings[0] is {Name: {} target,Value: {} template}
         &&SimpleName(target)&&ControlVectorExecution.IsLoopTemplate(template.Text))kind="loop_set";
      break;
    }
    return new(name,kind,analysis,settings,physical,kind=="unsupported"?"unsupported_control_command:"+name:null){GrammarRule=SpiceParser.ruleNames[item.RuleIndex],Postprocessing=postprocessing,Argument=argument??token.Text[(item.Start.StopIndex+1)..].Trim(),RedirectTarget=redirect,Foreach=foreachHeader};
   }catch(ParseCanceledException){return new(token.Text,"unsupported",null,[],physical,"unsupported_control_syntax");}
  }
 }
}
