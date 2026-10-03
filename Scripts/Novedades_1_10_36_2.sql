/*
    Novedades de la versión 1.10.36.2 (03/10/2026).

    Tanda: NestoAPI + Nesto (ClickOnce 1.10.36.2) + Ariadna (canal interno de Play Store).

    ORDEN:
      1) ANTES de publicar la API, como sa en NV (el orden entre ellos da igual):
           Scripts/Ariadna6_AnularLecturas.sql
           Scripts/Ariadna8_AvisosFichaProducto.sql
      2) Publicar NestoAPI.
      3) Publicar la ClickOnce 1.10.36.2 (Clean + Rebuild).
      4) DESPUÉS: ESTE script (idempotente: compara por Versión + Título).

    SE OMITE A PROPÓSITO (hoy solo lo nota quien prueba Ariadna; se contará cuando los mozos la usen):
      - Nesto#507: Agencias propone los bultos del packing de Ariadna y abre sus fotos.
      - Nesto#508: ningún albarán sale si el picking tiene faltas de Ariadna sin quitar.
      - Nesto#509 / Ariadna#8: avisos de «dato mal» en la ficha a Tienda online y Compras (correo + campana).
      - Ariadna#6 (anular lecturas), fichas de producto y fotos en api/Almacen, regla de portes de #556.
      - Novedades de Ariadna (Ambito='Ariadna'): ninguna aún; la usa solo Carlos.

    Ejecutar en SSMS contra NV. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.36.2'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-10-03';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000));

INSERT INTO @novedades (Categoria, Titulo, Descripcion) VALUES
    ('Mejorado', N'«Crear albarán» con una nota de entrega ya no da error',
     N'En el detalle del pedido, «Crear albarán» y «Crear albarán y factura» sobre una nota de entrega decían «El pedido es nota de entrega» y no hacían nada. Ahora preguntan y la procesan igual que Facturar rutas: la nota queda hecha y, si el producto estaba «en carpeta», se descuenta del stock. No imprime nada: eso sigue en Facturar rutas.'),
    ('Corregido', N'Ya no falla al guardar un pedido con líneas recién añadidas',
     N'Al añadir líneas a un pedido y guardar muy rápido, a veces salía «El campo Usuario es obligatorio» y el pedido no se guardaba. Ahora las líneas nuevas quedan a nombre de quien está editando el pedido; las que ya estaban conservan su autor.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa'
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;

-- Comprobación: deben salir 2 filas, sin repetidos.
SELECT Id, Version, Categoria, Titulo, Ambito, Publicada
FROM Novedades WHERE Version = @version ORDER BY Id;
