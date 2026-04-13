import { useCallback, useState } from "react";
import { useDropzone, type FileRejection } from "react-dropzone";
import { Upload, X, FileIcon } from "lucide-react";
import { ALLOWED_EXTENSIONS, MAX_FILE_SIZE_BYTES, MAX_FILE_SIZE_LABEL } from "@/lib/constants";
import { cn } from "@/lib/utils";

// Derive MIME type allow-list from extensions
const ALLOWED_MIME_TYPES: Record<string, string[]> = {
  "image/png": [".png"],
  "image/jpeg": [".jpg", ".jpeg"],
  "image/gif": [".gif"],
  "image/webp": [".webp"],
  "text/plain": [".txt", ".puml", ".mmd"],
  "text/markdown": [".md"],
  "application/xml": [".xml", ".drawio"],
  "text/xml": [".xml", ".drawio"],
};

interface UploadDropzoneProps {
  onFileSelected: (file: File) => void;
  selectedFile: File | null;
  onFileClear: () => void;
  error?: string | undefined;
}

export function UploadDropzone({ onFileSelected, selectedFile, onFileClear, error }: UploadDropzoneProps) {
  const [validationError, setValidationError] = useState<string | null>(null);

  const onDrop = useCallback(
    (accepted: File[], rejected: FileRejection[]) => {
      setValidationError(null);

      if (rejected.length > 0) {
        const firstError = rejected[0]?.errors[0];
        if (firstError?.code === "file-too-large") {
          setValidationError(`File is too large. Maximum size is ${MAX_FILE_SIZE_LABEL}.`);
        } else if (firstError?.code === "file-invalid-type") {
          setValidationError(
            `File type not supported. Allowed: ${ALLOWED_EXTENSIONS.join(", ")}`,
          );
        } else {
          setValidationError("File rejected. Please check the file type and size.");
        }
        return;
      }

      const file = accepted[0];
      if (!file) return;

      // Additional extension check (defense in depth)
      const ext = `.${file.name.split(".").pop()?.toLowerCase() ?? ""}`;
      if (!ALLOWED_EXTENSIONS.includes(ext as (typeof ALLOWED_EXTENSIONS)[number])) {
        setValidationError(`File type not supported. Allowed: ${ALLOWED_EXTENSIONS.join(", ")}`);
        return;
      }

      onFileSelected(file);
    },
    [onFileSelected],
  );

  const { getRootProps, getInputProps, isDragActive } = useDropzone({
    onDrop,
    maxSize: MAX_FILE_SIZE_BYTES,
    accept: ALLOWED_MIME_TYPES,
    multiple: false,
  });

  const displayError = validationError ?? error;

  if (selectedFile) {
    return (
      <div className="flex items-center gap-3 rounded-lg border bg-muted/50 p-4">
        <FileIcon className="h-6 w-6 shrink-0 text-primary" />
        <div className="flex-1 min-w-0">
          <p className="font-medium truncate">{selectedFile.name}</p>
          <p className="text-xs text-muted-foreground">
            {(selectedFile.size / 1024 / 1024).toFixed(2)} MB
          </p>
        </div>
        <button
          onClick={onFileClear}
          className="text-muted-foreground hover:text-foreground transition-colors"
          aria-label="Remove file"
        >
          <X className="h-4 w-4" />
        </button>
      </div>
    );
  }

  return (
    <div className="space-y-2">
      <div
        {...getRootProps()}
        className={cn(
          "flex cursor-pointer flex-col items-center gap-3 rounded-lg border-2 border-dashed p-10 text-center transition-colors",
          isDragActive
            ? "border-primary bg-primary/5"
            : "border-muted-foreground/25 hover:border-primary/50 hover:bg-muted/30",
          displayError && "border-destructive",
        )}
      >
        <input {...getInputProps()} />
        <Upload className="h-8 w-8 text-muted-foreground" />
        <div>
          <p className="font-medium">
            {isDragActive ? "Drop the file here" : "Drag and drop or click to upload"}
          </p>
          <p className="mt-1 text-xs text-muted-foreground">
            Supported: {ALLOWED_EXTENSIONS.join(", ")} · Max {MAX_FILE_SIZE_LABEL}
          </p>
        </div>
      </div>

      {displayError && (
        <p className="text-sm text-destructive" role="alert">
          {displayError}
        </p>
      )}
    </div>
  );
}
