# Data fencing

Security context is supplied by authenticated host code and is not part of query JSON. Its allowed branches, roles and permissions are copied into immutable sets. Core rejects missing identifiers, an active branch outside the allowed set, missing tool rights, unknown metrics, invalid dates and excessive row limits.

v0.1 natural-language queries operate on the active branch. Cross-branch requests are rejected. The host determines which branch is active; changing a prompt cannot change it.

The data adapter must revalidate tenant/branch/user membership using its trusted data source. The synthetic connector checks a fixed synthetic identity registry and filters by scope before computing totals. Core additionally rejects any returned result whose tenant, branch or user differs from the authenticated context.

Combined briefs require `business.read` plus sales, inventory, customers and expenses rights. Other tools use their specific registry permission. Adapters may enforce stricter host rights.

Tests include forged scope, missing permission, attempts to override instructions, secret requests, SQL writes, unknown tools, duplicate JSON properties and oversized ranges. These tests verify application controls; they do not claim a prompt filter can recognize every malicious sentence.
