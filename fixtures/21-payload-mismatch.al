module fixture.payload_mismatch;
pub union Choice { Value(i32), Other }
pub fn bad() -> Choice effects {} { return Choice.Value("wrong"); }
