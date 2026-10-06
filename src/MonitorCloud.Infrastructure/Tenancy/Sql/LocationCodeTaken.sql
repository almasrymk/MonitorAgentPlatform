-- Is the location code used by another location of the same tenant (ignores the caller's location scope)?
SELECT CAST(CASE WHEN EXISTS (
    SELECT 1
    FROM tenancy.Locations
    WHERE TenantId = @TenantId
      AND Code = @Code
      AND (@ExceptId IS NULL OR Id <> @ExceptId)
) THEN 1 ELSE 0 END AS bit);
