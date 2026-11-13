"""Bounded in-memory multipart uploads shared by batch and saved verification."""
from email import policy
from email.parser import BytesParser


async def read_multipart(request, field_limits):
    try:
        length = int(request.headers.get("content-length", "0"))
    except ValueError:
        raise ValueError("Send Content-Length and at most 26 MiB.") from None
    if request.headers.get("transfer-encoding") or not 0 < length <= 26 * 1024 * 1024:
        raise ValueError("Send Content-Length and at most 26 MiB.")
    content_type = request.headers.get("content-type", "")
    if "\r" in content_type or "\n" in content_type or not content_type.lower().startswith("multipart/form-data;"):
        raise ValueError("Expected multipart/form-data.")
    body = bytearray()
    async for part in request.stream():
        if len(body) + len(part) > length:
            raise ValueError("Upload exceeded Content-Length.")
        body.extend(part)
    if len(body) != length:
        raise ValueError("Incomplete upload.")
    try:
        header = ("Content-Type: " + content_type + "\r\nMIME-Version: 1.0\r\n\r\n").encode("ascii")
    except UnicodeEncodeError:
        raise ValueError("Invalid multipart content type.") from None
    message = BytesParser(policy=policy.default).parsebytes(header + body)
    if not message.is_multipart() or message.defects:
        raise ValueError("Invalid multipart upload.")
    fields, files = {}, []
    for part in message.iter_parts():
        name = part.get_param("name", header="content-disposition")
        value = part.get_payload(decode=True)
        if value is None or part.defects:
            raise ValueError("Invalid form data.")
        if name == "file":
            files.append(value)
        elif name in field_limits:
            if name in fields or len(value) > field_limits[name]:
                raise ValueError("Duplicate or oversized field.")
            try:
                fields[name] = value.decode("utf-8")
            except UnicodeDecodeError:
                raise ValueError("Form fields must be UTF-8 text.") from None
    if len(files) != 1 or not files[0]:
        raise ValueError("Exactly one audio file is required.")
    return fields, files[0]
