# Productos: la ficha

Documentación de NestoAPI para Claude, por temas (el índice está en `CLAUDE.md`). Lo que pinta la ficha de producto de Nesto y no es de
compras, almacén ni precios. Lo nuevo de este tema se apunta aquí, en su sección o en una nueva; en `CLAUDE.md` solo se toca su línea del índice.
(La próxima entrada de la ficha, #869, está en `compras.md`.)

## Referencia anterior y siguiente en la ficha (sugerencia 1478, Manuel, 26/11/26; gemela Nesto)
- `GET api/Productos/Vecinos?producto=45957&empresa=1&saltarAnuladas=true` (`saltarAnuladas` opcional, false por defecto; Nesto lo manda siempre:
  las de Estado negativo o sin estado no cuentan como vecinas) (`VecinosProductoController`, `[Authorize]`, solo lectura; cliente de la tienda 403;
  400 sin producto; 404 si no existe) → {Producto, Anterior, Siguiente} (null = no hay). Nesto pinta dos flechas junto a la referencia.
- LA REGLA (`ReglaVecinosProducto`, `Infraestructure/Productos/VecinosProducto.cs`): una referencia NUMÉRICA (solo cifras, 1..9) tiene por
  vecinas la numérica inmediatamente menor y mayor por su VALOR, saltando huecos; una no numérica (MF…, IMPE…), por orden alfabético entre
  las no numéricas. Cuentan todas las fichas de la empresa, también las anuladas. Una ida a la BD (`LectorVecinosProductoSql`).
- OJO: la BD no tiene `TRY_CONVERT` (nivel de compatibilidad antiguo: «no es un nombre de función integrada reconocido»): se convierte
  dentro de un `CASE WHEN <solo cifras> THEN CONVERT(int, …) END`.
