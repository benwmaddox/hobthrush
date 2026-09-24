# Grammar

The parser accepts the core pure-language slice below. The wider V1 syntax in [the PRD](PRD.md) remains a design target; unsupported declarations and statements fail with a diagnostic.

```ebnf
module          = "module", qualified_name, ";", { declaration } ;
declaration     = [ "pub" ], ( function | union ) ;

function        = "fn", identifier, "(", [ parameters ], ")",
                  "->", type, "effects", "{", "}", block ;
parameters      = parameter, { ",", parameter }, [ "," ] ;
parameter       = identifier, ":", type ;

type            = qualified_name, [ "<", type, { ",", type }, ">" ] ;

union           = "union", identifier, "{", [ variant, { ",", variant }, [ "," ] ], "}" ;
variant         = identifier, [ "(", payload_fields, ")" ] ;
payload_fields  = named_fields | positional_fields ;
named_fields    = named_field, { ",", named_field }, [ "," ] ;
named_field     = identifier, ":", type ;
positional_fields = type, { ",", type }, [ "," ] ;

block           = "{", { statement }, "}" ;
statement       = "let", identifier, ":", type, "=", expression, ";"
                | "return", expression, ";" ;

expression      = match_expression | additive ;
additive        = multiplicative, { ("+" | "-"), multiplicative } ;
multiplicative  = unary, { "*", unary } ;
unary           = [ "-" ], primary ;
primary         = integer | boolean | text | identifier | call
                | variant_construction | "(", expression, ")" ;
call            = identifier, "(", [ arguments ], ")" ;
variant_construction
                = identifier, ".", identifier, [ "(", [ arguments ], ")" ] ;
arguments       = expression, { ",", expression }, [ "," ] ;

match_expression = "match", expression, "{", match_arm, { ",", match_arm }, [ "," ], "}" ;
match_arm       = pattern, "=>", expression ;
pattern         = "_"
                | [ identifier, "." ], identifier, [ "(", [ bindings ], ")" ] ;
bindings        = identifier, { ",", identifier }, [ "," ] ;

integer         = digit, { digit } ;
boolean         = "true" | "false" ;
text            = '"', { character | escape }, '"' ;
escape          = '\"' | '\\' | '\n' | '\r' | '\t' | '\0' ;
qualified_name  = identifier, { ".", identifier } ;
identifier      = letter | "_", { letter | digit | "_" } ;
```

A union variant uses either named fields such as `TooLong(max: i32)` or positional fields such as `Value(i32)`. One variant cannot mix the two forms. User-defined generic unions and functions are not supported. `Option<T>` and `Result<T, E>` are compiler-provided generic types; their type arguments must be supported types.

The implemented value types are `i32`, `bool`, `Text`, declared non-generic unions, `Option<T>`, and `Result<T, E>`. Integer `+`, `-`, and `*` use checked `i32` arithmetic. Text literals support the escapes shown above.

Use `Choice.Yes` or `Choice.Value(3)` to construct a declared union. Built-in constructors use `Some(value)`, `None`, `Ok(value)`, and `Err(error)`. `None`, `Some`, `Ok`, and `Err` need an expected type from a local annotation, return type, or function parameter. A same-named user function takes precedence. Fully qualified `Option<T>.Some` and `Result<T, E>.Ok` construction is deferred.

A match covers every union variant, including `Some`/`None` or `Ok`/`Err`; `_` is an explicit wildcard. Payload pattern names bind the payload values and do not need to match named field labels. Match arms use `=>` and are comma-separated.

Every function still writes an explicit `effects {}` clause. Only empty effect sets are implemented. Function bodies currently accept typed `let` declarations and `return` statements.

## Build and run entrypoints

`lang build FILE` emits an executable when the source contains `fn main()` with no parameters and a return type of `i32`, `bool`, or `Text`. Otherwise it emits a library, including when a function named `main` has another signature. Build artifacts are saved under an `out/<stem>-<id>/` directory beside the source file.

`lang run FILE` requires that executable entry signature. The program prints its returned value followed by a newline; `i32`, lowercase `bool`, and `Text` values are printed directly. Successful runs exit 0. Checked i32 arithmetic overflow prints a generic runtime-fault message and exits 70.
