# PhantomSystem localization

The Inspector, diagnostics, and generated Expression Menus share LanguagePrefs.Language
from NDMF. Switching the language updates open PhantomSystem editors and existing
NDMF diagnostic views. Menus use the selected language when the avatar is rebuilt.
English is the fallback language.

Each UTF-8 JSON file contains an entries array of stable key / value pairs.
Keep all three catalogs in sync. Use {0}, {1}, etc. for complete sentence
templates and retain the same placeholders in every language. A .tooltip key
provides the tooltip for the corresponding label.

Use PhantomLocalization.D for diagnostics stored in validation results, parameter
plans, or build reports. It retains the key and arguments until display time;
nested PhantomDiagnostic values and lists also follow the current language.
Use S/F for text displayed immediately, including exception messages. Diagnostic
logic must use fixed keys or codes, never translated text. Warning entries use
NDMF's NonFatal severity and do not enter the blocking error list.

Parameter identifiers, diagnostic codes, animation paths, serialized field names,
and source-avatar menu content are intentionally not translated. Exception types,
stack traces, and messages from external libraries remain original technical details.
Unity Console history keeps the language used when a message was emitted.
Unity MenuItem paths remain fixed English commands. Translation files are editor-only.

After editing translations, use NDMF/Modular Avatar's localization reload command
or reload editor assemblies. Editor tests validate catalog keys, format placeholders,
shared language selection, live diagnostic views, non-blocking warnings, error
deduplication, and generated parameter names.
