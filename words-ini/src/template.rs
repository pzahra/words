//! A value's pieces, read once in the order written (Core SPEC: One pass for
//! every brace), as Core's `WordsTemplate.Parse` reads them.

//a number or an alignment from here on is none, where string.Format stops
const LIMIT_DIGITS: usize = 7;

/// A piece of a value. Each knows where it sits, so a piece left as written
/// shows exactly what the file said.
#[derive(Clone, Debug, PartialEq, Eq)]
pub(crate) struct Piece<'a> {
    pub written: &'a str,
    pub kind: Kind<'a>,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub(crate) enum Kind<'a> {
    /// Text, each escaped brace (`{{`) one brace.
    Text(String),
    /// `{>key}`: the key's words, rendered against it.
    Reference(&'a str),
    /// `{$name}`: the constant `$name`'s value, as text.
    Constant(&'a str),
    /// `{0#word}` or `{Count#word}`: the form of the key its argument's count picks.
    Selector { argument: String, key: &'a str },
    /// `{0}` or `{Name}`, with an alignment and a spec, `{0,-5:N2}`.
    Item { argument: String, alignment: Option<i64>, format: Option<&'a str> },
    /// A brace that opens none of the others, through the brace that closes it.
    Malformed,
}

/// The pieces of `value`, covering it end to end.
pub(crate) fn parse(value: &str) -> Vec<Piece<'_>> {
    let mut pieces = Vec::new();
    let mut run = String::new();
    let mut start = 0;
    let mut at = 0;
    while at < value.len() {
        let Some(offset) = value[at..].find('{') else {
            run.push_str(&value[at..]);
            break;
        };
        let brace = at + offset;
        run.push_str(&value[at..brace]);
        at = brace;
        if value[at + 1..].starts_with('{') {
            run.push('{');
            at += 2;
            continue;
        }
        let (length, kind) = opened(value, at);
        if at > start {
            pieces.push(Piece { written: &value[start..at], kind: Kind::Text(std::mem::take(&mut run)) });
        }
        pieces.push(Piece { written: &value[at..at + length], kind });
        at += length;
        start = at;
    }
    if value.len() > start {
        pieces.push(Piece { written: &value[start..], kind: Kind::Text(run) });
    }
    pieces
}

fn char_at(text: &str, i: usize) -> Option<char> {
    text.get(i..).and_then(|rest| rest.chars().next())
}

//the piece the { at `at` opens, with the bytes it takes
fn opened(text: &str, at: usize) -> (usize, Kind<'_>) {
    let mut i = at + 1;
    if let Some(mark @ ('>' | '$')) = char_at(text, i) {
        let close = text[i + 1..].find(['{', '}']).map(|offset| i + 1 + offset);
        return match close {
            Some(close) if close > i + 1 && text.as_bytes()[close] == b'}' => {
                let key = &text[i + 1..close];
                let kind = if mark == '>' { Kind::Reference(key) } else { Kind::Constant(&text[i..close]) };
                (close + 1 - at, kind)
            }
            _ => malformed(text, at),
        };
    }

    //a number as string.Format reads it, spaces only after it; a name with any
    //space but a line break around it, as FormatByName reads one
    let numbered = char_at(text, i).is_some_and(|c| c.is_ascii_digit());
    i = skip(text, i, numbered);
    let argument_start = i;
    if numbered {
        while char_at(text, i).is_some_and(|c| c.is_ascii_digit()) {
            i += 1;
        }
    } else if char_at(text, i).is_some_and(|c| c.is_ascii_alphabetic() || c == '_') {
        i += 1;
        while let Some(c) = char_at(text, i).filter(|&c| is_word_char(c)) {
            i += c.len_utf8();
        }
    } else {
        return malformed(text, at);
    }
    let mut argument = text[argument_start..i].to_string();
    if numbered {
        match number(&argument) {
            Some(index) => argument = index.to_string(),
            None => return malformed(text, at),
        }
    }

    //a selector: the argument right inside the brace, a # and a key
    if char_at(text, i) == Some('#') {
        i += 1;
        let key_start = i;
        while let Some(c) = char_at(text, i).filter(|&c| !matches!(c, '{' | '}' | '#') && !c.is_whitespace()) {
            i += c.len_utf8();
        }
        if argument_start != at + 1 || i == key_start || char_at(text, i) != Some('}') {
            return malformed(text, at);
        }
        return (i + 1 - at, Kind::Selector { argument, key: &text[key_start..i] });
    }

    i = skip(text, i, numbered);
    let mut alignment = None;
    if char_at(text, i) == Some(',') {
        i = skip(text, i + 1, numbered);
        let left = char_at(text, i) == Some('-');
        if left {
            i += 1;
        }
        let digits = i;
        while char_at(text, i).is_some_and(|c| c.is_ascii_digit()) {
            i += 1;
        }
        let Some(width) = number(&text[digits..i]) else {
            return malformed(text, at);
        };
        alignment = Some(if left { -(width as i64) } else { width as i64 });
        i = skip(text, i, numbered);
    }

    //a spec runs to the }, and holds no brace; a name's no line break either
    let mut format = None;
    if char_at(text, i) == Some(':') {
        i += 1;
        let spec_start = i;
        while let Some(c) = char_at(text, i).filter(|&c| !matches!(c, '{' | '}') && (numbered || !matches!(c, '\r' | '\n'))) {
            i += c.len_utf8();
        }
        let spec = if numbered { &text[spec_start..i] } else { text[spec_start..i].trim() };
        if !spec.is_empty() {
            format = Some(spec);
        }
    }
    if char_at(text, i) != Some('}') {
        return malformed(text, at);
    }
    (i + 1 - at, Kind::Item { argument, alignment, format })
}

//digits as string.Format reads them, under ten million, any zeros before them aside
fn number(digits: &str) -> Option<u32> {
    if digits.is_empty() {
        return None;
    }
    let significant = digits.trim_start_matches('0');
    if significant.len() > LIMIT_DIGITS {
        return None;
    }
    let value: u32 = if significant.is_empty() { 0 } else { significant.parse().ok()? };
    (value < 10_000_000).then_some(value)
}

//a brace that opens nothing, through the brace that closes it, or alone if none does
fn malformed(text: &str, at: usize) -> (usize, Kind<'_>) {
    let mut depth = 0;
    for (i, b) in text.bytes().enumerate().skip(at) {
        if b == b'{' {
            depth += 1;
        } else if b == b'}' {
            depth -= 1;
            if depth == 0 {
                return (i + 1 - at, Kind::Malformed);
            }
        }
    }
    (1, Kind::Malformed)
}

//past the spaces at i: string.Format's around a number, any but a line break around a name
fn skip(text: &str, mut i: usize, numbered: bool) -> usize {
    while let Some(c) = char_at(text, i) {
        let space = if numbered { c == ' ' } else { c.is_whitespace() && !matches!(c, '\r' | '\n') };
        if !space {
            break;
        }
        i += c.len_utf8();
    }
    i
}

/// .NET's `\w`, which reads one UTF-16 unit at a time, as near as the standard
/// library tells: a letter, a digit, `_`, or a combining diacritic
/// (U+0300–U+036F, how a decomposed `é` is written). A character past the
/// 16-bit plane is none, as .NET sees two surrogates there. Other marks, and
/// numbers that are no digits (`²`, `①`), read otherwise than in .NET (SPEC:
/// *Rendering*).
pub(crate) fn is_word_char(c: char) -> bool {
    if (c as u32) > 0xFFFF {
        return false;
    }
    c.is_alphanumeric() || c == '_' || ('\u{300}'..='\u{36F}').contains(&c)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn kinds(value: &str) -> Vec<Kind<'_>> {
        parse(value).into_iter().map(|piece| piece.kind).collect()
    }

    fn text(s: &str) -> Kind<'static> {
        Kind::Text(s.to_string())
    }

    fn item(argument: &str, alignment: Option<i64>, format: Option<&'static str>) -> Kind<'static> {
        Kind::Item { argument: argument.to_string(), alignment, format }
    }

    #[test]
    fn each_piece_knows_its_place() {
        let value = "Hi {0}, {>name} has {$unit}{Count#file}";
        let pieces = parse(value);
        let written: Vec<&str> = pieces.iter().map(|p| p.written).collect();
        assert_eq!(written, ["Hi ", "{0}", ", ", "{>name}", " has ", "{$unit}", "{Count#file}"]);
        assert_eq!(pieces[3].kind, Kind::Reference("name"));
        assert_eq!(pieces[5].kind, Kind::Constant("$unit"));
        assert_eq!(pieces[6].kind, Kind::Selector { argument: "Count".into(), key: "file" });
        assert_eq!(written.concat(), value);
    }

    #[test]
    fn an_escaped_brace_is_one_and_a_closing_brace_always_itself() {
        assert_eq!(kinds("{{0}"), [text("{0}")]);
        assert_eq!(kinds("{{0}}"), [text("{0}}")]);
        assert_eq!(kinds("a } b"), [text("a } b")]);
        assert_eq!(kinds("{{0#word}"), [text("{0#word}")]);
        assert_eq!(kinds(""), []);
    }

    #[test]
    fn numbers_read_as_string_format_reads_them() {
        assert_eq!(kinds("{01}"), [item("1", None, None)]);
        assert_eq!(kinds("{0 ,-5 :N2}"), [item("0", Some(-5), Some("N2"))]);
        assert_eq!(kinds("{0,5}"), [item("0", Some(5), None)]);
        assert_eq!(kinds("{0:}"), [item("0", None, None)]);
        assert_eq!(kinds("{0: x }"), [item("0", None, Some(" x "))]);
        assert_eq!(kinds("{9999999}"), [item("9999999", None, None)]);
        assert_eq!(kinds("{10000000}"), [Kind::Malformed]);
        assert_eq!(kinds("{00000000001}"), [item("1", None, None)]);
    }

    #[test]
    fn names_trim_their_spaces_and_take_an_alignment() {
        assert_eq!(kinds("{ Name }"), [item("Name", None, None)]);
        assert_eq!(kinds("{Name,3: x }"), [item("Name", Some(3), Some("x"))]);
        assert_eq!(kinds("{_n}"), [item("_n", None, None)]);
        assert_eq!(kinds("{Größe}"), [item("Größe", None, None)]);
        assert_eq!(kinds("{Cafe\u{301}}"), [item("Cafe\u{301}", None, None)]);
        assert_eq!(kinds("{a\u{1D465}}"), [Kind::Malformed]);
    }

    #[test]
    fn every_malformed_shape_shows_as_written() {
        for value in
            ["{}", "{0,x}", "{ 0}", "{0:{>fmt}}", "{0:N{>digits}}", "{>}", "{$}", "{>a{b}", "{ 0#w}", "{0#}", "{0#a b}", "{-1}", "{0,}"]
        {
            let pieces = parse(value);
            assert_eq!(pieces[0].kind, Kind::Malformed, "{value}");
        }
        let pieces = parse("x {0:{>fmt}} y");
        assert_eq!(pieces[1].written, "{0:{>fmt}}");
        let pieces = parse("x { y");
        assert_eq!(pieces[1].written, "{");
        assert_eq!(pieces[2].kind, text(" y"));
    }

    #[test]
    fn a_selector_holds_the_argument_right_inside_the_brace() {
        assert_eq!(kinds("{0#.n}"), [Kind::Selector { argument: "0".into(), key: ".n" }]);
        assert_eq!(kinds("{007#word}"), [Kind::Selector { argument: "7".into(), key: "word" }]);
    }
}
