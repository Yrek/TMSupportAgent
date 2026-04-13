import { Link, useParams } from "react-router-dom";
import { Upload, PenLine, ArrowLeft } from "lucide-react";
import { AppShell } from "@/components/layout/AppShell";

export function SubmitJobPage() {
  const { orgId } = useParams<{ orgId: string }>();

  const options = [
    {
      to: `/orgs/${orgId!}/jobs/new/upload`,
      icon: <Upload className="h-10 w-10 text-primary" />,
      title: "Upload architecture file",
      description:
        "Upload a diagram, document, or markup file (.png, .jpg, .puml, .drawio, .xml, .md, and more). The AI will extract your architecture automatically.",
    },
    {
      to: `/orgs/${orgId!}/jobs/new/manual`,
      icon: <PenLine className="h-10 w-10 text-primary" />,
      title: "Draw manually",
      description:
        "Start with a blank canvas and add elements directly. Ideal when you want full control or don't have an existing diagram.",
    },
  ];

  return (
    <AppShell>
      <div className="mx-auto max-w-3xl space-y-6 p-6">
        <div className="flex items-center gap-3">
          <Link
            to={`/orgs/${orgId!}/jobs`}
            className="text-muted-foreground hover:text-foreground transition-colors"
            aria-label="Back to jobs"
          >
            <ArrowLeft className="h-5 w-5" />
          </Link>
          <h1 className="text-2xl font-bold">New analysis</h1>
        </div>

        <p className="text-muted-foreground">
          Choose how to provide your architecture. Both paths lead to the same review step before
          threat analysis begins.
        </p>

        <div className="grid gap-4 sm:grid-cols-2">
          {options.map((opt) => (
            <Link
              key={opt.to}
              to={opt.to}
              className="flex flex-col items-start gap-4 rounded-xl border bg-card p-6 transition-colors hover:border-primary hover:bg-primary/5"
            >
              {opt.icon}
              <div>
                <h2 className="font-semibold">{opt.title}</h2>
                <p className="mt-1 text-sm text-muted-foreground">{opt.description}</p>
              </div>
            </Link>
          ))}
        </div>
      </div>
    </AppShell>
  );
}
