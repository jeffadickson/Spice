parser grammar SpiceParser;
options { tokenVocab = SpiceLexer; }

deck: TITLE (NL | statement)* EOF;
statement: (passive | source | diode | bjt | mos | jfet | vcontrol |
    icontrol | switchCard | cswitch | coupling | subcall | behavior | lineCard |
    model | includeCard | paramCard | tran | ac | dc | op | extension | unknownDevice | endCard) NL
    | subcircuit | control;
passive: PASSIVE_NAME node node parameter+;
source: SOURCE_NAME node node parameter+;
diode: DIODE_NAME node node identifier parameter*;
bjt: BJT_NAME node node node node? node? identifier parameter*;
mos: MOS_NAME node node node node identifier parameter*;
jfet: JFET_NAME node node node identifier parameter*;
vcontrol: VCONTROL_NAME node node parameter+;
icontrol: ICONTROL_NAME node node identifier parameter+;
switchCard: SWITCH_NAME node node node node identifier parameter*;
cswitch: CSWITCH_NAME node node identifier identifier parameter*;
coupling: COUPLING_NAME identifier identifier parameter+;
subcall: SUBCALL_NAME node parameter+;
behavior: BEHAVIOR_NAME node node identifier EQUAL expression | BEHAVIOR_NAME node node parameter+;
lineCard: LINE_NAME node node node node parameter+;
model: MODEL identifier identifier parameter*;
includeCard: INCLUDE (STRING | QUOTED | WORD);
paramCard: PARAM assignment+;
tran: TRAN value value value? value? identifier?;
ac: AC identifier value value value;
dc: DC identifier value value value (identifier value value value)?;
op: OP;
endCard: END;
subcircuit: SUBCKT identifier parameter* NL (NL | statement)* ENDS identifier? NL;
control: CONTROL NL (COMMAND_LINE? NL)* ENDC NL;
// Extension structure is retained but reported as a grammar gap unless a named
// directive has a documented structural contract in the frontend inventory.
extension: DIRECTIVE parameter*;
// A preserved unimplemented device is a gap, never accepted_structure.
unknownDevice: WORD parameter*;

parameter: assignment | call | group | value | COMMA;
assignment: (identifier | call) EQUAL (value | group | call | expression);
call: identifier LPAREN parameter* RPAREN;
group: LPAREN parameter* RPAREN | LBRACKET parameter* RBRACKET;
value: (PLUS | MINUS)? NUMBER | identifier | STRING | QUOTED | LBRACE expression RBRACE;
node: (PLUS | MINUS)? NUMBER | identifier;
identifier: WORD | PASSIVE_NAME | SOURCE_NAME | DIODE_NAME | BJT_NAME |
    MOS_NAME | JFET_NAME | VCONTROL_NAME | ICONTROL_NAME | SWITCH_NAME |
    CSWITCH_NAME | COUPLING_NAME | SUBCALL_NAME | BEHAVIOR_NAME | LINE_NAME;

// Expressions are syntax only: no evaluation, unit checking or symbol lookup.
expression: (PLUS | MINUS | BANG) expression
    | expression POWER expression
    | expression (STAR | SLASH) expression
    | expression (PLUS | MINUS) expression
    | expression COMPARE expression
    | expression BOOL expression
    | expression QUESTION expression COLON expression
    | LPAREN expression RPAREN
    | identifier LPAREN (expression (COMMA expression)*)? RPAREN
    | NUMBER | identifier;
