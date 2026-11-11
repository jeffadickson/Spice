parser grammar SpiceParser;
options { tokenVocab = SpiceLexer; }

deck: TITLE (NL | statement)* EOF | script;
script: SCRIPT_TITLE scriptLine* EOF;
scriptLine: COMMAND_LINE? NL;
statement: (passive | source | diode | bjt | mos | jfet | vcontrol |
    icontrol | switchCard | cswitch | coupling | subcall | behavior | lineCard |
    xspice | osdi | cpl | txl | urc | model | function | includeCard | libraryCard | paramCard | tran | ac | dc | op | extension | unknownDevice | endCard) NL
    | subcircuit | control;
passive: PASSIVE_NAME twoNodes parameter+;
source: SOURCE_NAME twoNodes parameter*;
// Bounded semantic entry over the existing source-tail tokens. The general
// source rule continues accepting other syntactically valid source families.
sourceProfile: sourceClause+ EOF | {TokenStream.LT(1).Text.ToLowerInvariant() is not ("dc" or "ac" or "pulse" or "sin" or "pwl" or "exp" or "sffm" or "am" or "distof1" or "distof2")}? value sourceClause* EOF;
sourceClause: barePwlDc | dcExcitation | acExcitation | groupedPwlExcitation | waveformExcitation | distortionExcitation;
// Native accepts an explicit zero DC value spelled as bare DC before PWL.
barePwlDc: {TokenStream.LT(1).Text.Equals("dc",StringComparison.OrdinalIgnoreCase)
    && string.Equals(TokenStream.LT(2)?.Text,"pwl",StringComparison.OrdinalIgnoreCase)}? identifier;
groupedPwlExcitation: {TokenStream.LT(1).Text.Equals("pwl",StringComparison.OrdinalIgnoreCase)}? identifier
    LPAREN pwlPair (COMMA? pwlPair)* RPAREN;
pwlPair: LPAREN waveformScalar COMMA? waveformScalar RPAREN;
// Waveform fields are signed scalar values, not general whitespace arithmetic.
// Delimited/braced expressions retain their existing generated value context.
waveformExcitation: {TokenStream.LT(1).Text.ToLowerInvariant() is "pulse" or "sin" or "pwl" or "exp" or "sffm" or "am"}? identifier
    (LPAREN waveformValues RPAREN | waveformValues);
waveformValues: waveformScalar (COMMA? waveformScalar)*;
waveformScalar: {TokenStream.LT(1).Text.ToLowerInvariant() is not ("dc" or "ac" or "distof1" or "distof2")}? value;
dcExcitation: {TokenStream.LT(1).Text.Equals("dc",StringComparison.OrdinalIgnoreCase) && !string.Equals(TokenStream.LT(2)?.Text,"pwl",StringComparison.OrdinalIgnoreCase)}? identifier EQUAL? value;
acExcitation: {TokenStream.LT(1).Text.Equals("ac",StringComparison.OrdinalIgnoreCase)}? identifier (EQUAL acScalar acScalar? | acScalar acScalar?)?;
acScalar: {TokenStream.LT(1).Text.ToLowerInvariant() is not ("dc" or "ac" or "pulse" or "sin" or "pwl" or "exp" or "sffm" or "am" or "distof1" or "distof2")}? value;
// Distortion-only source coefficients are separate from ordinary waveforms.
distortionExcitation: {TokenStream.LT(1).Text.ToLowerInvariant() is "distof1" or "distof2"}? identifier NUMBER;
// Typed linear E/G tail; TABLE/POLY/behavioral headers remain outside this entry.
linearControlProfile: twoNodes value EOF;
// Bounded generated semantic entries over existing card tokens.
currentControlProfile: value EOF;
globalNodesProfile: node (COMMA? node)* EOF;
// A composite B header is decomposed only by the generated lexer in BODY mode.
// Resistor model references are identifiers; ordinary scalar evaluation retains precedence.
resistorModelProfile: identifier assignment* EOF;
behaviorHeaderProfile: BEHAVIOR_NAME twoNodes identifier EQUAL EOF;
// Scalar model arrays reuse existing value tokens; arithmetic members are braced.
scalarVectorProfile: LBRACKET (value ((COMMA | {TokenStream.LT(1).StartIndex > TokenStream.LT(-1).StopIndex + 1}?) value)*)? RBRACKET EOF;
diode: DIODE_NAME twoNodes referenceName parameter*;
bjt: BJT_NAME (threeNodes | fourNodes | fiveNodes) referenceName parameter*;
mos: MOS_NAME (threeNodes | fourNodes | fiveNodes | sixNodes | sevenNodes) referenceName parameter*;
jfet: JFET_NAME threeNodes referenceName parameter*;
// TABLE selects a strict tail only after E/G output nodes. The generated
// predicate prevents malformed TABLE tails falling back to generic parameters.
vcontrol: VCONTROL_NAME twoNodes (tableTail | {!(TokenStream.LT(1).Text.Equals("table",StringComparison.OrdinalIgnoreCase) && TokenStream.LA(2)==LBRACE)}? parameter+);
tableTail: {TokenStream.LT(1).Text.Equals("table",StringComparison.OrdinalIgnoreCase)}? identifier LBRACE expression RBRACE EQUAL? tableSeparator* tablePair+;
// ngspice inpcom.c TABLE uses gettok_node: commas/parentheses separate scalar
// coordinates, including legacy unmatched wrappers. Keep this tolerance local;
// the controlling expression and the even coordinate count remain strict.
tablePair: tableCoordinate tableSeparator* tableCoordinate tableSeparator*;
tableCoordinate: (PLUS | MINUS)? (NUMBER | identifier) | LBRACE expression RBRACE;
tableSeparator: LPAREN | RPAREN | COMMA;
icontrol: ICONTROL_NAME twoNodes referenceName parameter+;
switchCard: SWITCH_NAME fourNodes referenceName parameter*;
cswitch: CSWITCH_NAME twoNodes referenceName referenceName parameter*;
coupling: COUPLING_NAME identifier identifier parameter+;
// A target-only X header is a valid zero-port subcircuit call.
subcall: SUBCALL_NAME node parameter*;
behavior: BEHAVIOR_START expression parameter* | BEHAVIOR_NAME twoNodes identifier EQUAL expression | BEHAVIOR_NAME twoNodes parameter+;
function: FUNC_START EQUAL? expression;
lineCard: LINE_NAME fourNodes parameter+;
// Terminal count for OSDI and conductor dimension for CPL are model-dependent.
// Parsing preserves their node sequences; elaboration checks model contracts.
osdi: OSDI_NAME node+ referenceName parameter*;
cpl: CPL_NAME node node node node node* referenceName parameter*;
txl: TXL_NAME fourNodes referenceName parameter*;
urc: URC_DIGITAL_HEADER twoNodes parameter+
    | URC_NAME threeNodes referenceName parameter*
    | URC_NAME identifier (LPAREN NUMBER (COMMA NUMBER)* RPAREN)? twoNodes parameter+;
// Syntax from ngspice's code-model instance contract. Port/model compatibility
// is elaboration; parentheses/vector structure and differential arity are syntax.
xspice: XSPICE_NAME xport+ referenceName;
xport: TILDE? (XDIFF (xpair | xvector | LPAREN xvector RPAREN | scalarNode scalarNode)
    | XSINGLE (scalarNode | LPAREN scalarNode RPAREN | xvector | LPAREN xvector RPAREN)
    | XTYPE (scalarNode | LPAREN scalarNode RPAREN | xvector | LPAREN xvector RPAREN)
    | scalarNode | xvector | LPAREN xport+ RPAREN);
xpair: LPAREN scalarNode COMMA? scalarNode RPAREN;
xvector: LBRACKET xport+ RBRACKET;
// ngspice's model token readers treat outer parentheses as separators, including
// historical one-sided wrappers. Expression/call/complex delimiters stay strict.
model: MODEL referenceName identifier (modelVersionAssignment | assignment | call | value | COMMA | CARET identifier | LPAREN | RPAREN)*;
includeCard: INCLUDE (STRING | QUOTED | WORD);
libraryCard: LIB (STRING | QUOTED | WORD) identifier?;
paramCard: PARAM assignment+;
tran: TRAN value value value? value? identifier?;
ac: AC identifier value value value;
dc: DC identifier value value value (identifier value value value)?;
op: OP;
endCard: END;
subcircuit: SUBCKT referenceName parameter* NL (NL | statement)* ENDS parameter* NL;
control: CONTROL NL (COMMAND_LINE? NL)* ENDC NL;
// Extension structure is retained but reported as a grammar gap unless a named
// directive has a documented structural contract in the frontend inventory.
extension: DIRECTIVE parameter*;
// A preserved unimplemented device is a gap, never accepted_structure.
unknownDevice: WORD parameter*;

parameter: assignment | arithmetic | comparison | call | group | value | COMMA;
// Start arithmetic with an atom, keeping bare nested groups unambiguous.
arithmetic: (call | value) (STAR | SLASH | PLUS | MINUS | POWER) expression;
comparison: (call | value) (COMPARE | EQUAL | COMPLEX_OPEN | COMPLEX_CLOSE) (call | value);
// The lexer splits 3.3.0 into two NUMBER tokens. The second token must
// immediately follow the first, so an unrelated positional term stays opaque.
modelVersionAssignment: {string.Equals(TokenStream.LT(1)?.Text,"version",StringComparison.OrdinalIgnoreCase)
    && TokenStream.LA(3)==NUMBER && TokenStream.LA(4)==NUMBER
    && TokenStream.LT(3).StopIndex+1==TokenStream.LT(4).StartIndex
    && TokenStream.LT(3).Text.Split('.').Length==2
    && char.IsAsciiDigit(TokenStream.LT(3).Text[0]) && char.IsAsciiDigit(TokenStream.LT(3).Text[^1])
    && TokenStream.LT(3).Text.All(c=>char.IsAsciiDigit(c)||c=='.')
    && TokenStream.LT(4).Text.StartsWith(".",StringComparison.Ordinal)
    && TokenStream.LT(4).Text.Skip(1).All(char.IsAsciiDigit)}? identifier EQUAL NUMBER NUMBER;
assignment: (referenceName | call) EQUAL (concatenation | value | group | call | expression);
concatenation: (identifier | NUMBER | STRING | QUOTED | LBRACE expression RBRACE)
    (LBRACE expression RBRACE (identifier | NUMBER | STRING | QUOTED)?)+;
call: identifier LPAREN parameter* RPAREN;
group: LPAREN parameter* RPAREN | LBRACKET parameter* RBRACKET;
value: (PLUS | MINUS)? (NUMBER | identifier) | STRING | QUOTED | LBRACE expression RBRACE | complexValue;
complexValue: COMPLEX_OPEN value COMMA? value COMPLEX_CLOSE;
// Brackets are part of ordinary device node spelling, but remain connection
// vector delimiters in XSPICE's distinct token contract (mifutil.c).
node: scalarNode (LBRACKET scalarNode RBRACKET)*;
scalarNode: (PLUS | MINUS)? (NUMBER | identifier);
referenceName: identifier | NUMBER;
twoNodes: node COMMA? node | LPAREN node COMMA? node RPAREN;
threeNodes: node COMMA? node COMMA? node | LPAREN node COMMA? node COMMA? node RPAREN;
fourNodes: node COMMA? node COMMA? node COMMA? node | LPAREN node COMMA? node COMMA? node COMMA? node RPAREN;
fiveNodes: node node node node node | LPAREN node node node node node RPAREN;
sixNodes: node node node node node node | LPAREN node node node node node node RPAREN;
sevenNodes: node node node node node node node | LPAREN node node node node node node node RPAREN;
identifier: WORD | PASSIVE_NAME | SOURCE_NAME | DIODE_NAME | BJT_NAME |
    MOS_NAME | JFET_NAME | VCONTROL_NAME | ICONTROL_NAME | SWITCH_NAME |
    CSWITCH_NAME | COUPLING_NAME | SUBCALL_NAME | BEHAVIOR_NAME | LINE_NAME | XSPICE_NAME | NUMERIC_NAME | PATH_NAME |
    OSDI_NAME | CPL_NAME | TXL_NAME | URC_NAME;

// Voltage-reference node arguments have their own generated lexical context.
voltageReference: EXPR_VREF_OPEN referenceNode (COMMA referenceNode)? RPAREN;
referenceNode: WORD | STRING;

// Recognition stays syntax-only. The semantic evaluator reuses this tree.
// Pinned ngspice parameter fixtures: power binds before unary minus, while
// chained powers associate left (2^3^2 = 64, -2^2 = -4).
expression: expression (POWER | CARET) expression
    | (PLUS | MINUS | BANG | TILDE) expression
    | expression (STAR | SLASH | PERCENT) expression
    | expression (PLUS | MINUS) expression
    | expression LBRACKET expression RBRACKET
    | expression (COMPARE | EQUAL | COMPLEX_OPEN | COMPLEX_CLOSE | CONTROL_EQ | CONTROL_NE) expression
    | expression AND expression
    | expression OR expression
    | <assoc=right> expression QUESTION expression COLON expression
    | LPAREN expression RPAREN
    | LBRACE expression RBRACE
    | voltageReference
    | identifier LPAREN (expression (COMMA expression)*)? RPAREN
    | NUMBER | identifier | STRING | QUOTED | CONTROL_VARIABLE;


// A bounded semantic entry over generated BODY tokens of one COMMAND_LINE.
// Numeric command arguments deliberately exclude vectors, substitution and state.
controlCommandProfile: (controlOp | controlAc | controlDc | controlTran | controlRun | controlSet | controlLet | controlCompose | controlDefine | controlUnlet | controlIf | controlForeach | controlBoundary | controlEcho | controlRedirect | controlPowerVectorType | controlReport | controlQuit | controlUnknown)? EOF;
controlOp: {TokenStream.LT(1).Text.Equals("op",StringComparison.OrdinalIgnoreCase)}? identifier;
controlAc: {TokenStream.LT(1).Text.Equals("ac",StringComparison.OrdinalIgnoreCase)}? identifier identifier controlNumber controlNumber controlNumber;
controlDc: {TokenStream.LT(1).Text.Equals("dc",StringComparison.OrdinalIgnoreCase)}? identifier identifier controlNumber controlNumber controlNumber (identifier controlNumber controlNumber controlNumber)?;
controlTran: {TokenStream.LT(1).Text.Equals("tran",StringComparison.OrdinalIgnoreCase)}? identifier controlNumber controlNumber controlNumber? controlNumber? identifier?;
controlRun: {TokenStream.LT(1).Text.Equals("run",StringComparison.OrdinalIgnoreCase)}? identifier;
controlSet: {TokenStream.LT(1).Text.Equals("set",StringComparison.OrdinalIgnoreCase)}? identifier parameter+;
// These productions preserve declarations; they do not evaluate result vectors
// or user functions. The requested compiler qualifies only a reporting suffix.
controlLet: {TokenStream.LT(1).Text.Equals("let",StringComparison.OrdinalIgnoreCase)}? identifier identifier EQUAL expression;
// Each complete expression must yield a scalar. Binary operators belong to
// that expression (native `values 1 -2 3` yields [-1,3]); commas separate items.
controlCompose: {TokenStream.LT(1).Text.Equals("compose",StringComparison.OrdinalIgnoreCase)}? identifier identifier {TokenStream.LT(1).Text.Equals("values",StringComparison.OrdinalIgnoreCase)}? identifier expression (COMMA? expression)*;
controlDefine: {TokenStream.LT(1).Text.Equals("define",StringComparison.OrdinalIgnoreCase)}? identifier identifier LPAREN (identifier (COMMA identifier)*)? RPAREN expression;
controlUnlet: {TokenStream.LT(1).Text.Equals("unlet",StringComparison.OrdinalIgnoreCase)}? identifier identifier+;
controlIf: {TokenStream.LT(1).Text.Equals("if",StringComparison.OrdinalIgnoreCase)}? identifier (expression | CONTROL_VARIABLE)?;
controlForeach: {TokenStream.LT(1).Text.Equals("foreach",StringComparison.OrdinalIgnoreCase)}? identifier identifier CONTROL_VARIABLE;
controlBoundary: {TokenStream.LT(1).Text.ToLowerInvariant() is "else" or "end"}? identifier;
controlEcho: {TokenStream.LT(1).Text.Equals("echo",StringComparison.OrdinalIgnoreCase)}? identifier (~(CONTROL_SEPARATOR | INVALID | SLASH_COMMENT))*;
controlRedirect: {TokenStream.LT(1).Text.Equals("print",StringComparison.OrdinalIgnoreCase)}? identifier identifier COMPLEX_CLOSE identifier;
controlPowerVectorType: {TokenStream.LT(1).Text.Equals("settype", System.StringComparison.OrdinalIgnoreCase)}? identifier identifier identifier;
controlReport: {TokenStream.LT(1).Text.ToLowerInvariant() is "print" or "plot" or "meas" or "measure" or "save" or "gnuplot" or "rusage" or "display" or "listing"}? identifier parameter*;
controlQuit: {TokenStream.LT(1).Text.Equals("quit",StringComparison.OrdinalIgnoreCase)}? identifier NUMBER?;
controlUnknown: identifier parameter*;
// INPevaluate accepts attached signs, including adjacent signed arguments,
// but a standalone sign separated from its number is not a literal argument.
controlNumber: ((PLUS | MINUS) {TokenStream.LT(-1).StopIndex + 1 == TokenStream.LT(1).StartIndex}?)? NUMBER;
