{
  description = "Self-updating Nix wrapper for GithubLauncher";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
  };

  outputs = { self, nixpkgs }:
    let
      systems = [
        "x86_64-linux"
      ];

      forAllSystems = fn:
        nixpkgs.lib.genAttrs systems
          (system: fn {
            inherit system;
            pkgs = import nixpkgs { inherit system; };
          });
    in
    {
      packages = forAllSystems ({ pkgs, system }: {
        githublauncher = pkgs.callPackage ./nix/githublauncher-wrapper.nix {};

        default = self.packages.${system}.githublauncher;
      });

      apps = forAllSystems ({ system, ... }: {
        githublauncher = {
          type = "app";
          program = "${self.packages.${system}.githublauncher}/bin/githublauncher";
        };

        default = self.apps.${system}.githublauncher;
      });
    };
}
