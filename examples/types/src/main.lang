module example.types;

pub union Response {
    Accepted(message: Text, valid: bool),
    Rejected(i32),
}

pub union ParseError { Invalid }

pub fn enabled() -> bool effects {} {
    return true;
}

pub fn display(response: Response) -> Text effects {} {
    return match response {
        Response.Accepted(message, valid) => message,
        Response.Rejected(code) => "rejected",
    };
}

pub fn option_text(value: Option<Text>) -> Text effects {} {
    return match value {
        Some(message) => message,
        None => "missing",
    };
}

pub fn result_text(value: Result<Text, ParseError>) -> Text effects {} {
    return match value {
        Ok(message) => message,
        Err(error) => "failed",
    };
}

pub fn absent() -> Option<Text> effects {} {
    return None;
}

pub fn failed() -> Result<Text, ParseError> effects {} {
    return Err(ParseError.Invalid);
}

pub fn main() -> Text effects {} {
    let is_enabled: bool = enabled();
    let response: Response = Response.Accepted("escaped quote: \"", is_enabled);
    let optional: Option<Text> = Some(display(response));
    let parsed: Result<Text, ParseError> = Ok(option_text(optional));
    let absent_message: Text = option_text(absent());
    let failure_message: Text = result_text(failed());
    return result_text(parsed);
}
