import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import ShellApp from "./ShellApp";

createRoot(document.getElementById("root")!).render(<StrictMode><ShellApp /></StrictMode>);
