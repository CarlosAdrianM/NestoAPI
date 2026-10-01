/*
    Novedades de la versión 1.10.36.0 (01/10/2026).

    Tanda: NestoAPI + Nesto (ClickOnce 1.10.36.0).

    ORDEN:
      1) ANTES de publicar la API: nada obligatorio. Issue580_CheckEstadoExigeAlbaran.sql ya se lanzó el 01/10.
         (Los tres de #547 —Issue547_PreciosMediosPendientes, Issue547_DeshacerFacturaCmp_FechaModificacion e
         Issue547_DeshacerAlbaranCmp_FechaModificacion— se pueden lanzar cuando se quiera: el incremental sigue apagado.)
      2) Publicar NestoAPI.
      3) Publicar la ClickOnce 1.10.36.0 (Clean + Rebuild).
      4) DESPUÉS: ESTE script (idempotente: compara por Versión + Título).
      5) Scripts/OneShot_20261001_CerrarSugerencias_1_10_36_0.ps1: pone la versión a lo que pidieron las compañeras en
         Novedades (NO se repite aquí) y les contesta en su hilo:
           - 450 (Laura): los cursos de la tienda online salen exentos (IVA de la ficha del producto).
           - 451 (Lidia): un picking no saca solo los regalos si lo que se paga sigue pendiente.
      6) Piloto de la ventana de diálogos propia SOLO para Carlos (Nesto#490): el bloque del final de este script.

    SE OMITE A PROPÓSITO (el usuario de Nesto no lo nota):
      - Nesto#490 (4C.2 paso 3): ventana de diálogos propia, en piloto solo para Carlos.
      - #552: la sombra de GLS (la API construye la petición y la compara; no envía nada).
      - #553: GET api/Reposiciones/Propuesta (lectura, nadie lo llama aún). #547: deshacer albarán de compra recalcula la media.
      - #584: los clientes de fuera de la UE dejan de dar avisos falsos de NIF incorrecto (lo notan administración y tiendas,
        pero es la desaparición de un aviso; va en «Corregido» abajo como una línea, sin tecnicismos).
      - Una factura de 0 € de una ruta propia ya no se da por fallida al pasarla al extracto de ruta (interno).
      - GET api/Inventarios sin conteo da 404 en vez de error; ELMAH de validación con el campo; Ariadna (app de almacén).
      - Tests, issues, refactorizaciones.

    Ejecutar en SSMS contra NV. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.36.0'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-10-01';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000));

INSERT INTO @novedades (Categoria, Titulo, Descripcion) VALUES
    ('Nuevo', N'Al hacer el albarán, Nesto pregunta cuándo se entrega lo que queda en «carpeta»',
     N'Cuando al hacer el albarán desde el detalle del pedido queda producto recogido pendiente de entregar (lo que se llama «producto en carpeta»), Nesto crea la nota de entrega automática y pregunta la fecha en la que se va a entregar. Si todavía no se sabe, se deja sin fecha y se le puede poner después. Así esas notas ya no se quedan con una fecha que no es la real.'),
    ('Mejorado', N'Se puede cambiar el cliente de una nota de entrega',
     N'El botón «Cambiar cliente…» del detalle del pedido también funciona en las notas de entrega. En una nota solo se cambian el cliente, el contacto y la ruta, porque no se factura. Además, la nota de entrega automática de un renting o leasing va directamente al cliente final que figura en el contrato.'),
    ('Mejorado', N'Administración también puede crear albaranes y facturas desde el pedido',
     N'Los usuarios del grupo Administración tienen ya los botones para crear el albarán y la factura de venta desde el detalle del pedido, igual que almacén y tiendas.'),
    ('Mejorado', N'Si un pedido no se puede guardar, Nesto dice qué dato falla',
     N'Cuando un pedido no se podía guardar por un dato que no cumplía alguna regla, el aviso era genérico. Ahora dice qué campo es y por qué, para poder corregirlo sin llamar a nadie.'),
    ('Corregido', N'Al unir pedidos, Nesto explica por qué no se puede',
     N'Si la unión de dos pedidos no estaba permitida (por ejemplo, unir sobre una nota de entrega), el aviso decía solo «Forbidden». Ahora dice el motivo, por ejemplo «No se puede ampliar una nota de entrega».'),
    ('Corregido', N'Clientes de fuera de la Unión Europea: sin avisos falsos de NIF incorrecto',
     N'Al hacer un pedido o una factura a un cliente con país fiscal fuera de la UE (por ejemplo Venezuela o México) y su documento de identidad del país, llegaban avisos de «NIF incorrecto» a la tienda, a administración y al vendedor, y había que marcarlo a mano. Ahora, si el país de la ficha no es España ni de la UE, se trata directamente como documento extranjero y no hay avisos. Si es un pasaporte, administración lo sigue pudiendo marcar como tal.'),
    ('Corregido', N'Los portes y la comisión de reembolso ya no se quedan sin albarán',
     N'En algunos pedidos las líneas de portes o de comisión de reembolso nacían en albarán pero sin número de albarán, y al facturar la factura salía sin albarán. Ya no puede pasar.'),
    ('Corregido', N'Las cuentas bancarias del cliente no se guardan dos veces',
     N'En la ficha del cliente, al pulsar «Guardar Cambios» de las cuentas bancarias varias veces seguidas podía salir un error. El botón se desactiva mientras se guarda.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa'
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;

-- Comprobación: deben salir 8 filas de este script (más las 2 de las compañeras cuando se lance el paso 5), sin repetidos.
SELECT Id, Version, Categoria, Titulo, Ambito, Publicada
FROM Novedades WHERE Version = @version ORDER BY Id;

-- Paso 6 (Nesto#490): piloto de la ventana de diálogos propia SOLO para Carlos. Idempotente.
-- Vuelta atrás: UPDATE ParametrosUsuario SET Valor = '0' WHERE Empresa = '1' AND Usuario = 'Carlos' AND Clave = 'VentanaDialogosPropia';
IF EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Usuario = 'Carlos' AND Clave = 'VentanaDialogosPropia')
    UPDATE ParametrosUsuario SET Valor = '1', [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Usuario = 'Carlos' AND Clave = 'VentanaDialogosPropia';
ELSE
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'VentanaDialogosPropia', 'Carlos', '1', 'Carlos', GETDATE());

SELECT Clave, Usuario, Valor FROM ParametrosUsuario WHERE Clave = 'VentanaDialogosPropia';
