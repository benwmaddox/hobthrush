# Draft grammar

This draft fixes the shape of V1 syntax; productions outside the implemented slice remain design fixtures.

```ebnf
module         = "module", qualified_name, ";", { import | declaration } ;
import         = "import", qualified_name, ";" ;
declaration    = [ "pub" ], ( function | union | struct | trait | implementation | command | route ) ;
function       = [ "async" ], "fn", identifier, [ type_parameters ], "(", [ parameters ], ")",
                 "->", type, "effects", "{", [ effect_list ], "}", block ;
parameters     = parameter, { ",", parameter } ;
parameter      = identifier, ":", type ;
type_parameters= "<", identifier, { ",", identifier }, ">" ;
type           = qualified_name, [ "<", type, { ",", type }, ">" ] ;
union          = "union", identifier, [ type_parameters ], "{", variant, { ",", variant }, [ "," ], "}" ;
variant        = identifier, [ "(", [ parameters ], ")" ] ;
struct         = "struct", identifier, [ type_parameters ], "{", { parameter, ";" }, "}" ;
trait          = "trait", identifier, "{", { function_signature }, "}" ;
implementation = "impl", identifier, "for", type, "{", { function }, "}" ;
block          = "{", { statement }, "}" ;
statement      = ( "let" | "var" ), identifier, [ ":", type ], "=", expression, ";"
               | "return", expression, ";"
               | "with", expression, "as", identifier, block
               | "if", expression, block, [ "else", block ]
               | expression, ";" ;
expression     = literal | identifier | call | "match", expression, "{", { match_arm }, "}"
               | expression, binary_operator, expression | "await", expression ;
match_arm      = pattern, "=>", expression, "," ;
effect_list    = qualified_name, { ",", qualified_name } ;
command        = "command", identifier, "{", { command_item }, "}" ;
route          = "route", method, string, "{", { route_item }, "}" ;
```

The initial compiler accepts module headers, public/private `fn`, `i32`, typed parameters, integer literals, local `let`, calls, `+`, `-`, `*`, `return`, and empty effects. The remaining productions are design decisions for later gates and are not silently accepted by the compiler.
