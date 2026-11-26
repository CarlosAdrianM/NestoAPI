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

## Referencia del proveedor y extracto del producto (sugerencia 1482, Manuel, Compras; OK de Carlos 26/11/26; gemela Nesto)
- `FichaProductoController` (solo lectura, `[Authorize]`; cliente de la tienda 403; 400 sin producto; 404 si el producto no existe en la empresa).
  Todo en `Infraestructure/Productos/ExtractoProductoFicha.cs` (DTO, reglas puras y `LectorFichaProductoSql`, ADO con NOLOCK).
- `GET api/Productos/ReferenciasProveedor?producto=&empresa=1` → [{Proveedor, NombreProveedor, ReferenciaProveedor, Orden, EsHabitual}] de
  `ProveedoresProducto` (`ReferenciaProv`, `Orden`; el nombre de `Proveedores` de la misma empresa, el contacto de la fila primero), en UNA consulta.
  Habitual = el de menor Orden (`ReglaExtractoProducto.MarcarHabitual`; con empate, el primero por proveedor). Lista vacía si no tiene.
  SOLO Compras, Dirección e Informática (Carlos 18/11/26: de quién se compra; `PermisosFichaProducto.PuedeVerProveedoresYCostes` = la de
  `ComprasPlazosProveedoresController.PuedeVer`); los demás, 403 de usted. Nesto: línea bajo la familia con la referencia del habitual.
- `GET api/Productos/Extracto?producto=&empresa=1&almacen=&desde=&hasta=&pagina=1&tamano=200` → {Producto, Nombre, Almacen (null = todos),
  Desde, Hasta, SaldoInicial, SaldoFinal, Total, Pagina, Tamano, Movimientos [{NumeroOrden, Empresa, Fecha, Almacen, Cantidad, Saldo, Importe, Texto,
  Diario, Albaran, Proveedor, NombreProveedor, Cliente, ContactoCliente, NombreCliente, Pedido, LineaPedido, Traspaso, AsientoAutomatico, Delegacion,
  FormaVenta, Usuario}]}. ExtractoProducto de la empresa y su espejo (1 y 3: el stock de la casa suma las dos; cada fila dice la suya), del más
  nuevo al más antiguo (Fecha, Fecha Modificación, Empresa, Nº Orden), sin fechas los últimos 12 meses, días completos; tamaño ≤ 500 (200 por
  defecto). `Saldo` = acumulado del más antiguo al más nuevo dentro del filtro de almacén: LA REGLA `ReglaExtractoProducto.CalcularSaldos`
  (saldo del más nuevo de la página = SaldoInicial + lo del periodo − lo de las páginas anteriores). `Usuario` sin dominio. ExtractoProducto
  no guarda el número de asiento: `AsientoAutomatico` es la marca «Asiento Automático». Nombres de cliente y proveedor, de la misma empresa del
  movimiento o, si no está, de la empresa pedida.
- Quién: empleados de Compras, Almacén, Dirección, Informática y Tiendas (`PuedeVerExtracto`; Administración NO: Carlos 26/11/26, «¿para qué?»); un vendedor de NestoApp o un
  cliente de la tienda, 403. `Importe` (coste), `Proveedor` y `NombreProveedor` solo Compras, Dirección e Informática (a los demás, null).
- UNA ida con dos lotes (`SQL_EXTRACTO`): nombre del producto, saldo inicial, suma y número del periodo y suma de lo de las páginas anteriores;
  después la página con los nombres por `TOP 1` correlacionado SOLO sobre las filas de la página. Sin OFFSET/FETCH ni SUM() OVER (ORDER BY)
  (la BD va con un nivel de compatibilidad antiguo, como el TRY_CONVERT de arriba): `ROW_NUMBER()`.
- Nesto: botón «Extracto» en la ficha que abre una VENTANA APARTE (como el extracto de cliente; irá creciendo) con filtros de almacén y
  fechas, paginada; columnas Importe y Proveedor solo si llegan.
