/*
    Novedades de la versión 1.10.53.1 de Nesto (31/10/2026). Revisión de la 1.10.53 A PROPÓSITO (Carlos): la ventana de
    Novedades junta en una página las revisiones de una misma versión (Nesto#549), así quien no abrió Nesto el 30/10 ve las de
    la 1.10.53.0 y las de hoy juntas, sin tocar «Versión anterior».
    NestoAPI #770 (firma sellada), #771 (seguimiento con trazabilidad y fotos del packing), #772 (furgoneta bulto a bulto),
    #773 (cobro con tarjeta y autorización de reembolsos sin cobrar), #775 (notificaciones urgentes) + Nesto #590, #591, #593,
    el visor de fotos del packing y los globos.

    ORDEN:
      1) Scripts sa (YA LANZADOS 31/10): Issue770_FirmasBlob, Issue771_FotosPackingYSeguimiento, Issue772_FurgonetaBultos,
         Issue773_CobroTarjetaReparto. Contenedor «firmas» creado.
      2) Scripts/Globos.sql (sa) y publicar NestoAPI (con #774 globos y #775 urgentes); reciclar el pool. Activar los globos
         de ejemplo de Globos.sql (revisar textos) después de publicar las apps.
      3) Publicar la ClickOnce (Clean + Rebuild) → 1.10.53.1 y Ariadna 2.27 EL MISMO DÍA (con #773, la Ariadna 2.26 no puede
         entregar un reembolso sin cobrar).
      4) DESPUÉS: ESTE script y Novedades_Ariadna_2_27.sql. Luego AvisarNuevaVersionNesto.ps1 -Version 1.10.53.1 (con el OK de Carlos).

    Ejecutar en SSMS contra NV como sa. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.53.1';
DECLARE @fecha DATE = '2026-10-31';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Autorizar un reembolso sin cobrar desde Nesto',
     N'Cuando un repartidor no puede cobrar un reembolso, pide autorización desde Ariadna y a Administración le aparece al momento una ventana roja en la esquina, con pitido, con el cliente, el importe y el motivo, y los botones Autorizar y Denegar. En cuanto uno contesta, la ventana se cierra en todos con quién lo ha hecho. Si en dos minutos nadie contesta, se avisa también a Dirección. El cliente está esperando en la puerta: conteste cuanto antes.',
     N'Administración,Dirección'),
    ('Nuevo', N'Reposiciones: la recogida se ve sola',
     N'En «Enviar reposición» ya no hay que pulsar «El repartidor ya se la ha llevado»: el repartidor lee los bultos al recogerlos y Nesto enseña al momento cuántos lleva («Recogidos 6 de 7») y, al leer el último, quién se la ha llevado y a qué hora. Con «Seguimiento» se ve la historia de cada bulto: preparado, cargado, entregado, recogido, descargado en Algete y recibido.',
     N'Tiendas,Almacén,Dirección'),
    ('Nuevo', N'Bultos que se quedan en la furgoneta',
     N'Si un repartidor termina la jornada con algún bulto en la furgoneta (o a las 22:00 no la ha terminado), Almacén y Dirección reciben un aviso en la campana con la lista.',
     N'Almacén,Dirección'),
    ('Mejorado', N'Seguimiento del envío para el cliente',
     N'El enlace de seguimiento de nuestra ruta enseña ahora todo el recorrido del pedido: cuándo lo hizo, cuándo se empezó a preparar, cuándo se embaló, cuándo salió en la furgoneta y cuándo se entregó, con las fotos del packing. Si el pedido va en varios envíos, los demás salen aparte, plegados y con su propio enlace.',
     NULL),
    ('Mejorado', N'Fotos del packing en el detalle del pedido',
     N'En el visor de bultos del pedido, las fotos del packing que no son de un bulto concreto salen en su propio grupo, con Ver foto, Descargar y Copiar enlace.',
     N'Almacén,Administración,Dirección'),
    ('Nuevo', N'Globos con las novedades',
     N'Cuando Nesto estrena algo, la primera vez que abra esa ventana le sale un globo señalando lo nuevo. «Entendido» lo quita para siempre; Esc lo deja para la próxima vez que abra Nesto.',
     NULL);

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;
