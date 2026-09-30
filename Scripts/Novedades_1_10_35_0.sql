/*
    Novedades de la versión 1.10.35.0 (30/09/2026).

    Tanda: NestoAPI + Nesto (ClickOnce 1.10.35.0).

    ORDEN:
      1) ANTES de publicar la API: nada (Issue556_Ariadna_EscaneosYBultos.sql e Issue574_Ariadna_EscaneosTipoOrigen.sql
         ya se lanzaron el 30/09).
      2) Publicar NestoAPI.
      3) Publicar la ClickOnce 1.10.35.0 (Clean + Rebuild).
      4) DESPUÉS: ESTE script (idempotente: compara por Versión + Título).
      5) Scripts/OneShot_20260930_CerrarSugerencias_1_10_35_0.ps1: pone la versión a lo que pidieron los compañeros
         en Novedades (NO se repite aquí) y les contesta en su hilo:
           - 433 (Magan): la factura en PDF enseña el impagado.
           - 434 (Laura): «Copiar Factura» para tienda online.
           - 435 (Enrique): la ventana de «Copiar Factura» cabe en la pantalla.
           - 436 (Paloma): aviso del saldo a favor al mandar el cobro por tarjeta.

    SE OMITE A PROPÓSITO (el usuario de Nesto no lo nota):
      - #556 / #559 / #553 / #574 / #575: el servidor de Ariadna, la app de almacén (rutas que todavía no usa nadie).
      - #568: el aviso de versión nueva va por tandas; la respuesta del asistente llega también al supervisor.
      - #573: pedir el PDF de una factura que no existe devuelve un aviso en vez de un error interno.
      - #567: script de la traza para ver qué lee el Nesto viejo al imprimir.
      - Nesto#490 (4C.2): los últimos diálogos (Novedades, campana, código duplicado) sin cambio visible.
      - Tests, issues, refactorizaciones.

    Ejecutar en SSMS contra NV. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.35.0'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-09-30';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000));

INSERT INTO @novedades (Categoria, Titulo, Descripcion) VALUES
    ('Mejorado', N'Se puede apuntar un prepago en un pedido que ya tiene picking',
     N'Hasta ahora Nesto no dejaba añadir un prepago a un pedido con el picking sacado («anule el picking primero»). Un prepago es dinero ya cobrado, así que ya se puede apuntar sin anular nada. El único caso en el que sigue sin dejar es cuando la etiqueta de la agencia ya está impresa con reembolso, porque la agencia se lo cobraría otra vez al cliente.'),
    ('Mejorado', N'Una devolución no se factura antes que su venta',
     N'Si se intenta facturar la devolución de un producto cuya venta sigue en albarán (por ejemplo, un pedido de fin de mes todavía sin facturar), Nesto avisa y no la factura, diciendo el pedido y el albarán de la venta. Primero hay que facturar la venta: una rectificativa tiene que decir qué factura rectifica, y si no, no se puede declarar a Hacienda.'),
    ('Mejorado', N'Resumen diario de rapports: dice de quién es cada cosa y quién no ha metido ninguno',
     N'El correo con el resumen diario de rapports nombra siempre al vendedor del que habla, lista quién no ha metido ningún rapport ese día y señala los rapports hechos a clientes de otro vendedor. Los rapports guardados sin vendedor salen a nombre de quien los tecleó.'),
    ('Mejorado', N'Administración: el correo de Verifactu explica el motivo',
     N'Cuando una factura no se puede declarar, el correo diario a administración dice por qué y qué hay que hacer (por ejemplo, «es la devolución de una venta que todavía no está facturada», con el pedido, el albarán y la fecha límite), en vez de remitir a un registro técnico.'),
    ('Corregido', N'Un cliente recién dado de alta sale en el buscador',
     N'Al buscar un cliente creado ese mismo día, no aparecía hasta el día siguiente, ni siquiera por su número. Ya sale en el momento, y si se busca por el número exacto, sale el primero.'),
    ('Corregido', N'Guardar la ficha de un cliente ya no da error por el vendedor del grupo',
     N'Al guardar algunas fichas de cliente salía el error «El campo Usuario es obligatorio» y no se guardaba nada. Corregido.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa'
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;

-- Comprobación: deben salir 6 filas de este script (más las 4 de los compañeros cuando se lance el paso 5), sin repetidos.
SELECT Id, Version, Categoria, Titulo, Ambito, Publicada
FROM Novedades WHERE Version = @version ORDER BY Id;
