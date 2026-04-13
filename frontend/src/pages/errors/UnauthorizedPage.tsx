import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { ShieldOff } from "lucide-react";

export function UnauthorizedPage() {
  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-6 p-8 text-center">
      <ShieldOff className="h-16 w-16 text-muted-foreground" />
      <div>
        <h1 className="text-3xl font-bold">Access denied</h1>
        <p className="mt-2 text-muted-foreground">
          You don't have permission to access this page.
        </p>
      </div>
      <Button asChild variant="outline">
        <Link to="/">Go home</Link>
      </Button>
    </div>
  );
}
