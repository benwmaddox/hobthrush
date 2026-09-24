module fixture.effect;
fn read(fs: FsRead) -> Text effects { fs.read } { return fs.read_text("x"); }
pub fn bad(fs: FsRead) -> Text effects {} { return read(fs); }
