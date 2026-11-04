/*
    04/11/26: globos de Nesto 1.10.55 para la ficha de producto (Nesto#617): la casilla de la referencia ya busca como las
    líneas del pedido (referencia, código de barras o parte del nombre) y una referencia en el filtro de nombre + Intro abre
    directamente su ficha. Pantalla «Producto» (ProductoView), anclas Producto.Referencia y Producto.FiltroNombre.
    Orden: los siguientes al mayor que haya para Nesto (10 en los de 31/10 → 20 y 30). Duran 45 días. Para todos (Grupos NULL).
    Ejecutar en SSMS contra NV como sa (o nuevavision: solo INSERT). Idempotente: no repite un globo con la misma Aplicación + Ancla.
*/
SET NOCOUNT ON;
USE NV;

DECLARE @hoy date = CAST(GETDATE() AS date);
DECLARE @fin date = DATEADD(day, 45, @hoy);
DECLARE @orden int = ISNULL((SELECT MAX(Orden) FROM dbo.Globos WHERE Aplicacion = 'Nesto'), 0);

DECLARE @g TABLE (Aplicacion varchar(20), VersionDesde varchar(23), Pantalla varchar(100), Ancla varchar(200),
                  Titulo nvarchar(200), Texto nvarchar(1000), Grupos nvarchar(500), Orden int);
INSERT INTO @g VALUES
('Nesto', '1.10.55', 'Producto', 'Producto.Referencia', N'Buscar desde la referencia',
 N'Aquí puede teclear la referencia, leer el código de barras o escribir parte del nombre y elegir de la lista.',
 NULL, @orden + 10),
('Nesto', '1.10.55', 'Producto', 'Producto.FiltroNombre', N'Una referencia en el filtro de nombre',
 N'Si teclea una referencia aquí e Intro, se abre directamente su ficha.',
 NULL, @orden + 20);

INSERT INTO dbo.Globos (Aplicacion, VersionDesde, Pantalla, Ancla, Titulo, Texto, Grupos, FechaDesde, FechaHasta, Orden)
SELECT g.Aplicacion, g.VersionDesde, g.Pantalla, g.Ancla, g.Titulo, g.Texto, g.Grupos, @hoy, @fin, g.Orden
FROM @g g
WHERE NOT EXISTS (SELECT 1 FROM dbo.Globos x WHERE x.Aplicacion = g.Aplicacion AND x.Ancla = g.Ancla);
SELECT @@ROWCOUNT AS GlobosInsertadosAhora;

SELECT Id, Aplicacion, VersionDesde, Pantalla, Ancla, Orden, FechaDesde, FechaHasta, Activo FROM dbo.Globos
WHERE Aplicacion = 'Nesto' ORDER BY Pantalla, Orden;
