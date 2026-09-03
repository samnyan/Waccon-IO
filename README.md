# Waccon

Waccon is a modular virtual controller and IO bridge for WACCA (`mercury`) using segatools' custom `mercuryio` interface. It is intended to replace serial loopback-based controller stacks with native hook-based input and LED output.

Development currently starts with `waccon-io`, an ABI-compatible custom mercuryio DLL.

## Repository layout

- `waccon-io/` — custom mercuryio module and tests.
- `docs/` — Usage documents.
