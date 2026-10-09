/*
    Novedades de la versión 1.10.40.0 de Nesto (09/10/2026) y de la 2.4 de Ariadna.
    Versión de la ClickOnce: 1.10.40.0 (pubxml 1.10.40.* en revisión 0). Ariadna: ApplicationDisplayVersion 2.4 (Play interna y Windows, 09/10 14:25).

    Tanda: NestoAPI (#617 porcentajes de Rapports por filtro, Nesto#521 sugerencias del equipo + rol Dirección, #602 cupo de CTT,
    #577 job: corte cubierto, día ya tratado, calendario editable y recepción solo de lo que ha salido, #606 fecha prometida en
    el correo del pedido, #619 reentrenamiento mensual en Hangfire, #588 cierre del cliente según ruta propia, #581 sustitución
    temporal de referencias, sugerencia 551 novedades por perfil, sugerencia 564 PDF de la reposición, sugerencia 565 facturas
    físicas, portes de la serie EV, #593 Cheque Regalo c4 canje + aviso por correo + app de clientas, Ariadna para tiendas)
    + Nesto (Nesto#520 Eva Visnú, Nesto#521 desplegable de vendedor, #490 7.º tramo y 4C.2, mensaje del cupo de CTT,
    calendario de reposiciones, sustitución de referencias, novedades por perfil, imprimir y sonido en reposiciones, #593 c5
    cheque regalo en plantilla y pedido, Nesto#522 fotos del packing en el detalle)
    + Ariadna 2.4 (salida guiada: el código comprueba el producto que toca; modo tienda para Reina y Alcobendas).

    ORDEN:
      1) Scripts sa ANTES o junto con la API (LANZADOS HOY): Issue606_FechaEntregaAgenciaPrometida, Issue577_ReposicionesTraspasos,
         Issue577_UsuariosRellenarReposicionManual, Nesto521_RolDireccionIdentity (AspNetIdentity), Issue581_ProductosSustituciones,
         Sugerencia551_NovedadesPerfiles, Issue593_ChequeRegalo_c4, Issue593_ChequeRegalo_Aviso, Issue593_ChequeRegalo_TNV,
         OneShot_20261009_prdCrearFacturaVta_Estado95Nuevos (+ repaso estado 95). Pendiente de lanzar: OneShot_20261009_InnovatransSinSombra.
      2) En RDS2016, ANTES de publicar la API: crear C:\NestoAPI\ModelosIA y
         icacls C:\NestoAPI\ModelosIA /grant "IIS AppPool\Api:(OI)(CI)M"   (reentrenamiento #619).
      3) Publicar NestoAPI (si VS da error de project.assets.json: Restaurar paquetes NuGet). Comprobar C:\inetpub\Api\bin\lib_lightgbm.dll
         y en Hangfire los jobs «reentrenar-modelo-llamadas» y «cheques-regalo-push» y el servidor «RDS2016:entrenamiento».
      4) Tras publicar la API: OneShot_20261009_CalendarioTiendasAlgete9h_TrasDeploy.sql (cierre a las 9:00 de las tiendas → Algete),
         o cambiarlo en Nesto › Productos › Reposición › Calendario.
      5) Publicar la ClickOnce (Clean + Rebuild).
      6) DESPUÉS: ESTE script (idempotente: compara por Versión/Ámbito + Título).
      7) Marcar implementadas por la API (PUT api/Novedades/Sugerencias/{id} con Version y Perfiles), NO se insertan aquí:
         502 (Laura, pedidos de Eva Visnú con serie EV) → Administración; 549 (Alberto, clientes para contactar de su equipo) → Vendedores;
         551 (Alberto, novedades por perfil) → todos; 564 (Paloma, imprimir la reposición) → Tiendas; 565 (Alfredo, facturas físicas) → Almacén.
      8) Adjuntar la guía de reposiciones actualizada (Documentacion/Guia reposiciones automaticas.pdf) a la novedad de reposiciones
         de esta versión y a la de Ariadna 2.4 (Scripts/AdjuntarANovedad.ps1), y sustituirla en las 552 y 562.
      9) Reactivar las reposiciones automáticas que salen de Algete DESPUÉS de que Algete contabilice la de Alcobendas en Nesto viejo:
         OneShot_20261009_RepoAlgete_3_ReactivarAutomaticasDesdeAlgete.sql.
      Martes 13: activar la campaña del cheque (Activa = 1 + ChequesRegalo:Generar=true en el Web.config del repo y publicar).

    SE OMITE A PROPÓSITO:
      - Lo que va por las sugerencias de arriba (502, 549, 551, 564, 565).
      - Modernización #490 (7.º tramo y diálogos 4C.2), reentrenamiento del modelo (#619), estado 95 automático, job de reposiciones
        (corte cubierto, día ya tratado), portes de la serie EV, cupo de CTT en el servidor: internos o sin cambio visible.
      - La app de clientas (TNV 3.34.0) va por sus propias notas de Play.

    Ejecutar en SSMS contra NV como sa. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.40.0';
DECLARE @fecha DATE = '2026-10-09';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Cheque regalo de 50 € para el próximo pedido',
     N'Del 13 al 29 de octubre, la primera factura de cada cliente le genera un cheque regalo de 50 € (base imponible) para un pedido de más de 250 € de producto, hasta el 7 de noviembre. El cliente lo recibe por correo y en la factura. En la plantilla, al elegir un cliente con cheque, sale un aviso con la casilla «Usar el cheque regalo de 50 €», que lo añade al pedido y baja el total; si no llega al mínimo, Nesto dice cuánto falta. En el detalle de un pedido del cliente se puede añadir con un botón. Para el mínimo no cuentan portes, reembolso, cuotas, reparaciones, productos de peluquería ni packs «PACK 26» (sí pueden ir en el pedido). Un solo uso por cliente.',
     N'Vendedores,Tiendas,Administración'),
    ('Nuevo', N'Sustitución temporal de referencias',
     N'Compras puede indicar en la ficha de un producto (pestaña «Sustitución») que, mientras no haya stock o hasta una fecha, se sirva otro producto en su lugar, con el motivo. Al meter ese producto en la plantilla o en un pedido, Nesto pregunta «¿Poner la 45685 en su lugar?»: con «Sí» cambia las unidades al sustituto; con «No», no vuelve a preguntar por ese producto con ese cliente. La ficha enseña a todos la sustitución vigente.',
     N'Vendedores,Tiendas,Administración'),
    ('Nuevo', N'Las fotos de los bultos en el detalle del pedido',
     N'En la pestaña del seguimiento de la agencia del detalle del pedido salen los bultos que se prepararon con Ariadna: número, peso, quién lo cerró y cuándo. Si el bulto tiene foto (la que hace el almacén antes de cerrarlo), puedes verla, descargarla o copiar un enlace para mandárselo al cliente si reclama; cualquiera con ese enlace ve la foto.',
     N'Vendedores,Tiendas,Administración,Almacén'),
    ('Nuevo', N'Calendario de reposiciones desde Nesto',
     N'En Productos › Reposición, el botón «Calendario» enseña a qué hora se cierra y llega cada reposición entre Algete y las tiendas. Alfredo, Manuel y Carlos pueden cambiar las horas, añadir días y desactivar rutas; el resto lo ve. Cambiar la hora de cierre de hoy no repite la reposición de hoy: vale desde la siguiente.',
     N'Almacén,Tiendas'),
    ('Mejorado', N'La reposición que viene de Algete no se ve en la tienda hasta que sale',
     N'«Recibir reposición» ya no enseña una reposición de Algete mientras se está preparando: aparece cuando Algete la ha terminado y ha salido. Así no se puede recibir algo que aún no ha llegado.',
     N'Tiendas'),
    ('Mejorado', N'Enviar y recibir reposición avisan con un pitido si el código no cuadra',
     N'Al leer con el lector en «Enviar reposición» o «Recibir reposición», suena un pitido de error si el código no está en la reposición, si lo comparten varios productos o si no hay reposición abierta (y, al recibir, si ya se ha leído todo lo enviado). Tras cada lectura el cursor vuelve al lector. Útil con un lector inalámbrico, preparando lejos de la pantalla.',
     N'Tiendas'),
    ('Mejorado', N'Clientes para contactar: los porcentajes cambian con Visita, Teléfono o WhatsApp y con el subgrupo',
     N'En Rapports, al elegir el tipo de contacto o un subgrupo, los porcentajes de cada cliente y el orden de la lista se recalculan para ese filtro. La lista del día sigue siendo la misma (no se añade ni se quita nadie).',
     N'Vendedores'),
    ('Mejorado', N'El correo del pedido dice qué día se entrega a la agencia y por qué',
     N'El correo de un pedido nuevo enseña la fecha de entrega a la agencia que se le ha dicho al cliente y el porqué: lo que sale con stock, lo que espera una reposición de tienda (ruta y día) y lo que espera un pedido a proveedor (número y fecha prevista, en rojo si ya pasó). Al modificar un pedido, si la fecha cambia, sale resaltado «Antes… → Ahora…».',
     N'Vendedores,Administración'),
    ('Mejorado', N'El picking tiene en cuenta si el pedido va por nuestra ruta',
     N'Si el cliente cierra el día en que le llegaría el pedido, el picking mira ahora el día de entrega de nuestra ruta cuando el pedido lleva ruta propia (16 o AT), no el de la agencia. Y si un pedido por agencia se queda fuera porque el cliente cierra, el aviso dice si por nuestra ruta llegaría con el cliente abierto y cómo sacarlo.',
     N'Almacén'),
    ('Corregido', N'«Actualizar estado» de CTT ya no se queda sin respuesta por el límite de consultas',
     N'Cuando CTT limita las consultas de seguimiento, Nesto busca el envío por otro camino y, si aun así no puede, lo dice claro («CTT limita las consultas…; vuelve a intentarlo dentro de un rato») en lugar de un error técnico.',
     N'Almacén');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;

-- Ariadna 2.4
DECLARE @ariadna TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000));

INSERT INTO @ariadna (Categoria, Titulo, Descripcion) VALUES
    ('Mejorado', N'Al preparar, Ariadna comprueba que coges el producto que toca',
     N'En las salidas (reposiciones y picking) Ariadna elige la línea, por orden de huecos. Ve al hueco, coge ese producto y lee su código: si es otro producto sale «No coincide» con pitido de error y no cuenta. Si no tiene código, teclea su referencia. Hasta comprobarlo no deja meter la cantidad. Con cantidad 1 basta la lectura y pasa sola a la siguiente; con más, lee más veces o teclea la cantidad. «No está» (F3) para una falta. En las entradas no cambia nada: se lee cualquier producto en cualquier orden y Ariadna lo busca.'),
    ('Nuevo', N'Ariadna en las tiendas, para las reposiciones',
     N'En el móvil de Reina y de Alcobendas, Ariadna enseña solo Salidas y Entradas con sus reposiciones: enviar la reposición a Algete leyendo con la cámara (con la misma comprobación de producto) y recibir la que llega de Algete.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT '2.4', @fecha, a.Categoria, a.Titulo, a.Descripcion, 'Ariadna', 1, 'sa'
FROM @ariadna a
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = a.Titulo);

SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
