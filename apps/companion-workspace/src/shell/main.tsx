import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import ShellApp from "./ShellApp";
import "./theme.css";

createRoot(document.getElementById("root")!).render(<StrictMode><ShellApp /></StrictMode>);
