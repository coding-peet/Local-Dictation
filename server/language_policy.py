"""Whisper language policy shared by transport and original-weight inference."""
import re


def normalize_language(language):
    if language is None:
        return None
    if not isinstance(language, str):
        raise ValueError("Language must be a supported language code or auto.")
    language = language.strip().lower()
    language = {"slovak": "sk", "english": "en", "spanish": "es",
                "auto": None, "": None}.get(language, language)
    if language is not None and not re.fullmatch(r"[a-z]{2,3}", language):
        raise ValueError("Language must be a supported language code or auto.")
    return language


def preferred_for_model(languages, preferred, automatic=True):
    """Intersect explicit Auto preferences; never silently fall back to all languages."""
    if preferred is None:
        return None
    if not isinstance(preferred, list) or not 1 <= len(preferred) <= 128:
        raise ValueError("preferred_languages must be a nonempty list of at most 128 language codes.")
    normalized = []
    for value in preferred:
        code = normalize_language(value)
        if code is None:
            raise ValueError("Auto preferences must contain explicit language codes.")
        if code not in normalized:
            normalized.append(code)
    supported = [code for code in normalized if code in languages and code != "auto"]
    if automatic and not supported:
        raise ValueError("The selected model supports none of the preferred Auto languages.")
    return supported
