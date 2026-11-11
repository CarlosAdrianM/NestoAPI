/*
    Novedades de la revisión 1.10.60.1 (11/11/2026, tarde): despliegue SOLO de la API, junto con NestoApp 2.28.0.
    Van como revisión de la 1.10.60 para que salgan en la misma página que las de la mañana (la ventana pagina por
    Major.Minor.Build). Nesto no se publica: quien abra «Novedades» las ve; no salta el aviso de arranque.

    ORDEN:
      1) Scripts/Issue495_UltimaNovedadVista.sql (fila «(defecto)» de UltimaNovedadVistaApp y UltimaNovedadVista). Antes o después
         de la API, pero ANTES de publicar NestoApp 2.28.0: sin ella, el aviso de novedades de la app no hace nada.
      2) Publicar la API (master).
      3) ESTE script.
      4) NestoApp 2.28.0 (push a Master, su scripts/Novedades_2.28.0.sql y promoción a Production): lo lleva la sesión de NestoApp.

      Las novedades de la app («Recoge en tienda» en la plantilla y en el pedido, el aviso de novedades al entrar, el WhatsApp
      firmado, contestar la devolución desde el pedido) van en el script de NestoApp con ámbito NestoApp: aquí no se repiten.

      Se omiten a propósito (nada perceptible para el usuario de Nesto o va por otro lado): GET api/Novedades con «ambitos» y
      «desdeId» (NestoAPI#495; el lado Nesto queda pendiente), el fallo del servidor de correo apuntado en ELMAH y el vendedor del
      token en las facturas mandadas a mano (NestoAPI#854), y el campo «RecogeEnTienda» del pedido (NestoAPI#571, cortes 1 y 2:
      en Nesto no hay casilla todavía; el picking de recogidas y la impresión en ruta AM son los cortes 3 y 4).

    Ejecutar en SSMS contra NV como sa. Idempotente (compara por versión y título). Los literales van con N'...'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.60.1';
DECLARE @fecha DATE = '2026-11-11';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Mejorado', N'Pedidos que el cliente recoge en tienda: sin comisión contra reembolso',
     N'Un pedido con ruta de recogida en tienda (AM en Algete, ALC en Alcobendas o REI en Reina) ya no lleva comisión contra reembolso, además de no llevar portes: el cliente paga al recoger y no hay agencia que cobre. Si un pedido así tenía ya la línea de comisión, desaparece la próxima vez que se guarde.',
     NULL),
    ('Mejorado', N'WhatsApp del pedido o de la factura: firmado con su nombre',
     N'El mensaje de WhatsApp con el enlace de descarga de un pedido o de una factura termina ahora con el nombre de quien lo envía («Un saludo, [su nombre], Nueva Visión»), igual que los correos enviados a mano. Si Nesto no sabe qué vendedor es usted, firma solo «Nueva Visión», como hasta ahora.',
     NULL),
    ('Mejorado', N'Envío devuelto: también se contesta desde NestoApp',
     N'Cuando la agencia devuelve un envío y Nesto pregunta si el cliente lo sigue queriendo, el vendedor puede contestarlo también en NestoApp, desde el propio pedido (a partir de la versión 2.28.0 de la app). El correo y el aviso ya lo indican.',
     NULL);

INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;
SELECT Id, Categoria, Titulo FROM dbo.Novedades WHERE Version = @version ORDER BY Id;
