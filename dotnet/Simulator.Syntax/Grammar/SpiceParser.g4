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
sourceProfile: dcExcitation acExcitation? EOF | acExcitation dcExcitation? EOF | value EOF;
dcExcitation: {TokenStream.LT(1).Text.Equals("dc",StringComparison.OrdinalIgnoreCase)}? identifier EQUAL? value;
acExcitation: {TokenStream.LT(1).Text.Equals("ac",StringComparison.OrdinalIgnoreCase)}? identifier value value?;
// Typed linear E/G tail; TABLE/POLY/behavioral headers remain outside this entry.
linearControlProfile: twoNodes value EOF;
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
model: MODEL referenceName identifier (assignment | call | value | COMMA | CARET identifier | LPAREN | RPAREN)*;
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
    | expression (COMPARE | EQUAL | COMPLEX_OPEN | COMPLEX_CLOSE) expression
    | expression AND expression
    | expression OR expression
    | <assoc=right> expression QUESTION expression COLON expression
    | LPAREN expression RPAREN
    | LBRACE expression RBRACE
    | voltageReference
    | identifier LPAREN (expression (COMMA expression)*)? RPAREN
    | NUMBER | identifier | STRING | QUOTED;

