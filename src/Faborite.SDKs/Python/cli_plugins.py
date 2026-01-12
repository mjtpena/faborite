#!/usr/bin/env python3
"""
Faborite CLI Plugin System
Issue #129

Allows users to extend the CLI with custom commands.

Usage:
    faborite plugin install my-plugin
    faborite plugin list
    faborite my-custom-command
"""

import sys
import importlib.util
from pathlib import Path
from typing import Dict, Callable

class PluginManager:
    def __init__(self):
        self.plugins: Dict[str, Callable] = {}
        self.plugin_dir = Path.home() / ".faborite" / "plugins"
        self.plugin_dir.mkdir(parents=True, exist_ok=True)
    
    def load_plugins(self):
        """Load all plugins from the plugin directory"""
        for plugin_file in self.plugin_dir.glob("*.py"):
            try:
                spec = importlib.util.spec_from_file_location(plugin_file.stem, plugin_file)
                if spec and spec.loader:
                    module = importlib.util.module_from_spec(spec)
                    spec.loader.exec_module(module)
                    
                    if hasattr(module, "register"):
                        module.register(self)
                        print(f"Loaded plugin: {plugin_file.stem}")
            except Exception as e:
                print(f"Failed to load plugin {plugin_file.stem}: {e}", file=sys.stderr)
    
    def register_command(self, name: str, handler: Callable):
        """Register a new CLI command"""
        self.plugins[name] = handler
        print(f"Registered command: {name}")
    
    def execute_command(self, name: str, *args, **kwargs):
        """Execute a registered command"""
        if name in self.plugins:
            return self.plugins[name](*args, **kwargs)
        else:
            raise ValueError(f"Unknown command: {name}")
    
    def list_plugins(self):
        """List all registered plugins"""
        return list(self.plugins.keys())

# Example plugin (save as ~/.faborite/plugins/hello.py)
"""
def register(manager):
    manager.register_command("hello", hello_command)

def hello_command(*args, **kwargs):
    print("Hello from custom plugin!")
    print(f"Args: {args}")
    print(f"Kwargs: {kwargs}")
"""

# Interactive REPL - Issue #130
class FaboriteREPL:
    def __init__(self):
        self.context = {}
        self.history = []
    
    def run(self):
        """Run the interactive REPL"""
        print("Faborite Interactive REPL")
        print("Type 'help' for commands, 'exit' to quit")
        print()
        
        while True:
            try:
                line = input("faborite> ").strip()
                
                if not line:
                    continue
                
                if line == "exit":
                    break
                
                if line == "help":
                    self.show_help()
                    continue
                
                if line == "history":
                    self.show_history()
                    continue
                
                # Execute command
                result = self.execute(line)
                if result is not None:
                    print(result)
                
                self.history.append(line)
                
            except KeyboardInterrupt:
                print("\nUse 'exit' to quit")
            except Exception as e:
                print(f"Error: {e}", file=sys.stderr)
    
    def execute(self, line: str):
        """Execute a command line"""
        parts = line.split()
        command = parts[0] if parts else ""
        args = parts[1:] if len(parts) > 1 else []
        
        if command == "sync":
            return f"Triggering sync for workspace: {args[0] if args else 'default'}"
        elif command == "list":
            return f"Listing tables in workspace: {args[0] if args else 'default'}"
        elif command == "profile":
            return f"Profiling table: {args[0] if args else 'unknown'}"
        else:
            return f"Unknown command: {command}"
    
    def show_help(self):
        """Show available commands"""
        print("Available commands:")
        print("  sync <workspace> <lakehouse>  - Trigger a sync operation")
        print("  list <workspace> <lakehouse>  - List tables")
        print("  profile <table>               - Profile a table")
        print("  history                       - Show command history")
        print("  help                          - Show this help")
        print("  exit                          - Exit REPL")
    
    def show_history(self):
        """Show command history"""
        for i, cmd in enumerate(self.history, 1):
            print(f"{i:3d}  {cmd}")

if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "repl":
        repl = FaboriteREPL()
        repl.run()
    else:
        manager = PluginManager()
        manager.load_plugins()
        print(f"Loaded plugins: {manager.list_plugins()}")
