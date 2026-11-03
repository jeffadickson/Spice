using System.Collections.Immutable;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Simulator.Syntax.Generated;
namespace Simulator.Syntax;
// Generated command tokens remain the lexical authority. No Split/regex scanner
// interprets commands; default deck lexing remains unchanged outside this entry.
internal static class ControlExtraction
{
 internal static ControlBlockSyntax Extract(SpiceParser.ControlContext control,int scope,Func<int,int,SourceSyntax> capture,Func<ParserRuleContext,SourceRange> range){
  SourceRange Token(IToken token)=>capture(token.StartIndex,token.StopIndex).Range;
  var lines=control.COMMAND_LINE();if(lines.Length>256)return new(scope,range(control),Token(control.CONTROL().Symbol),Token(control.ENDC().Symbol),[],"control_command_budget");
  var commands=lines.Select(line=>Command(line.Symbol)).ToImmutableArray();return new(scope,range(control),Token(control.CONTROL().Symbol),Token(control.ENDC().Symbol),commands);
  ControlCommandSyntax Command(IToken token){
   var physical=Token(token);if(token.Text.Length>4096)return new("","unsupported",null,[],physical,"control_command_length_budget");
   var lexer=new SpiceLexer(new AntlrInputStream(token.Text)){ControlSemantic=true};lexer.Mode(SpiceLexer.BODY);lexer.RemoveErrorListeners();var tokens=new CommonTokenStream(lexer);tokens.Fill();
   if(tokens.Size>256)return new("","unsupported",null,[],physical,"control_token_budget");
   var unsafeToken=tokens.GetTokens().FirstOrDefault(t=>t.Type is SpiceLexer.CONTROL_SEPARATOR or SpiceLexer.CONTROL_VARIABLE or SpiceLexer.INVALID or SpiceLexer.SLASH_COMMENT);
   if(unsafeToken!=null)return new("","unsupported",null,[],physical,"unsafe_control_separator_variable_or_token");
   tokens.Seek(0);var parser=new SpiceParser(tokens){ErrorHandler=new BailErrorStrategy()};parser.RemoveErrorListeners();
   SourceSyntax Capture(ParserRuleContext context)=>capture(token.StartIndex+context.Start.StartIndex,token.StartIndex+context.Stop.StopIndex);
   try{
    var tree=parser.controlCommandProfile();var item=tree.GetChild(0) as ParserRuleContext;
    if(item==null)return new("","comment",null,[],physical);
    AnalysisSyntax? analysis=null;var name=item.Start.Text.ToLowerInvariant();var kind="unsupported";ImmutableArray<ControlSettingSyntax> settings=[];
    switch(item){
     case SpiceParser.ControlOpContext:kind="analysis";analysis=new(scope,"op",[],[],physical);break;
     case SpiceParser.ControlAcContext ac:kind="analysis";analysis=new(scope,"ac",[ac.identifier(1).GetText()],ac.controlNumber().Select(Capture).ToImmutableArray(),physical);break;
     case SpiceParser.ControlDcContext dc:kind="analysis";analysis=new(scope,"dc",dc.identifier().Skip(1).Select(i=>i.GetText()).ToImmutableArray(),dc.controlNumber().Select(Capture).ToImmutableArray(),physical);break;
     case SpiceParser.ControlTranContext tran:kind="analysis";analysis=new(scope,"tran",tran.identifier().Skip(1).Select(i=>i.GetText()).ToImmutableArray(),tran.controlNumber().Select(Capture).ToImmutableArray(),physical);break;
     case SpiceParser.ControlRunContext:kind="run";break;
     case SpiceParser.ControlQuitContext:kind="quit";break;
     case SpiceParser.ControlReportContext:kind="report";break;
     case SpiceParser.ControlSetContext set:
      kind="set";settings=set.parameter().Select(p=>{
       if(p.value()?.identifier() is {} flag)return new ControlSettingSyntax(flag.GetText(),null,null,false,Capture(p).Range);
       if(p.assignment() is not {} assignment)return new(null,null,null,false,Capture(p).Range);
       var value=assignment.GetChild(2) as ParserRuleContext;var scalar=value as SpiceParser.ValueContext;
       return new ControlSettingSyntax(assignment.referenceName()?.GetText(),value==null?null:Capture(value),scalar?.identifier()?.GetText(),scalar?.NUMBER()!=null,Capture(p).Range);
      }).ToImmutableArray();break;
    }
    return new(name,kind,analysis,settings,physical,kind=="unsupported"?"unsupported_control_command:"+name:null);
   }catch(ParseCanceledException){return new(token.Text,"unsupported",null,[],physical,"unsupported_control_syntax");}
  }
 }
}
