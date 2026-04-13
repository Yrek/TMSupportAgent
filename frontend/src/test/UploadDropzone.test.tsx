import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, it, expect, vi } from "vitest";
import { UploadDropzone } from "@/components/jobs/UploadDropzone";
import { MAX_FILE_SIZE_BYTES } from "@/lib/constants";

function makeFile(name: string, size: number, type = "image/png"): File {
  const content = new Uint8Array(size);
  return new File([content], name, { type });
}

describe("UploadDropzone", () => {
  it("calls onFileSelected with an allowed file", async () => {
    const onFileSelected = vi.fn();
    render(
      <UploadDropzone
        onFileSelected={onFileSelected}
        selectedFile={null}
        onFileClear={vi.fn()}
      />,
    );

    const input = document.querySelector("input[type=file]") as HTMLInputElement;
    const file = makeFile("diagram.png", 1024, "image/png");
    await userEvent.upload(input, file);

    expect(onFileSelected).toHaveBeenCalledWith(file);
  });

  it("shows error for a disallowed extension", async () => {
    const onFileSelected = vi.fn();
    render(
      <UploadDropzone
        onFileSelected={onFileSelected}
        selectedFile={null}
        onFileClear={vi.fn()}
      />,
    );

    const input = document.querySelector("input[type=file]") as HTMLInputElement;
    const file = makeFile("evil.exe", 1024, "application/octet-stream");
    await userEvent.upload(input, file);

    expect(onFileSelected).not.toHaveBeenCalled();
  });

  it("shows error for a file exceeding 10 MB", async () => {
    const onFileSelected = vi.fn();
    render(
      <UploadDropzone
        onFileSelected={onFileSelected}
        selectedFile={null}
        onFileClear={vi.fn()}
      />,
    );

    const input = document.querySelector("input[type=file]") as HTMLInputElement;
    const file = makeFile("huge.png", MAX_FILE_SIZE_BYTES + 1, "image/png");
    await userEvent.upload(input, file);

    expect(onFileSelected).not.toHaveBeenCalled();
    expect(await screen.findByRole("alert")).toBeInTheDocument();
  });
});
