/*
    Novedades de la versión 1.10.57.0 de Nesto (06/11/2026).
    Nesto #628 (ficha → Pendientes de entregar), #629 (aplazar con el envío en la agencia), #630 (versión al conectar), #621 (restos de
    Prism), parte Nesto de NestoAPI#832 (asistentes en mixtos) y de #839 (varias unidades sin etiqueta);
    NestoAPI #726 (fix de Gestiones de CTT en producción + fecha y «Parado en vuelo»), #722 (aplazar: CTT / Almacén / correo a logística),
    #831 (etiqueta RP aunque el reparto sea otro día), #832 (enlace solo a las online de un mixto), #833 (push a la PDA de urgentes y
    Dirección), #834 (servidor solo imprime para Ariadna; ConectadosNesto), #835 (umbral por job del vigilante), #836 (resumen de rapports),
    #837/#838 (cheque: copia al vendedor y carta en papel, próxima campaña), #839, #840 (referencia grande en la etiqueta), #588 (ruta
    propia = mismo día), quick wins de ELMAH (correo de entrega, Verifactu, errores de cliente), seguridad #427/#429 y el enlace de
    factura desde NestoApp (solo sus clientes, ver todos, jefe de equipo).

    ORDEN:
      1) Scripts sa ANTES de publicar la API: Issue831_EnviosRepartoPropio_FechaEntrega.sql (FechaEntrega = Fecha en los RP pendientes:
         sin él, los RP tramitados hoy saldrían como «reparto de mañana» y no se podrían cargar) e Issue834_ImpresoraDocumentosPorUsuario.sql
         (impresora de documentos de los usuarios de Ariadna de Reina y Alcobendas; vale también después).
         Pendiente de Carlos: OneShot_20261106_Inventario_35145_Menos1.sql (fila 61→60; luego contabilizar ALG por la API).
      2) Publicar la API (master) → ClickOnce (Clean + Rebuild) → 1.10.57.0 → flujos de Ariadna 2.31.
      3) DESPUÉS de publicar la API: ESTE script, Novedades_Ariadna_2_31.sql y OneShot_20261106_CerrarSugerencias_1_10_57_0.ps1 (marca
         Implementadas las sugerencias y contesta en cada hilo; lo lanzo yo). Luego AvisarNuevaVersionNesto.ps1 -Version 1.10.57.0 solo con
         el OK de Carlos, y el aviso a Algete en la novedad 1072 (guía y vídeo de CTT).
      4) Antes de imprimir etiquetas de producto con la API nueva: una de prueba con Scripts/Issue762_EtiquetaPrueba.ps1 y leerla con la pistola (#840).
      NO duplicar: las sugerencias 1106 (Nesto#628), 1110 (#588 ruta propia), 1113 (#839), 1115 (#840) y 752 (#836) las marca Implementadas
      el OneShot (la sugerencia ES la novedad); aquí no llevan fila. Las de Ariadna (1104, 1109, 1111) van con la 2.31.
      Se omiten a propósito (nada perceptible en Nesto): Nesto#621 y #630, #834, #835, quick wins de ELMAH, #427/#429 (TNV), el enlace de
      factura desde NestoApp (va con la versión de NestoApp) y la parte Ariadna de #831/#839.

    Ejecutar en SSMS contra NV como sa. Idempotente (compara por versión y título). Los literales van con N'...'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.57.0';
DECLARE @fecha DATE = '2026-11-06';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Gracias, Alfredo, Reina, Andre, Santiago, Sancho y Pilar',
     N'Esta versión sale de lo que habéis contado hoy por «Novedades»: abrir los pedidos pendientes desde la ficha del producto y la ruta 16 que dejaba fuera a los que cierran el lunes (Alfredo), apuntar varias unidades sin etiqueta de una vez y la referencia grande en la etiqueta (Reina), sumar unidades en el inventario sin saberse el atajo (Andre y Santiago), el resumen de rapports que marcaba como ajeno a un cliente que era suyo (Sancho) y el cheque en los cursos (Pilar). Seguid contándonos.',
     NULL),
    ('Corregido', N'Agencias: los cuatro botones de CTT ya se encienden',
     N'Desde ayer los botones «Parar en vuelo», «Crear retorno», «Cambiar fecha» y «Cambiar dirección» salían apagados para todos los envíos de CTT («La agencia CTT no tiene gestión en tránsito») por un fallo nuestro al conectar la pieza de CTT. Ya funcionan. Además, al cambiar la fecha de entrega el envío queda con la fecha nueva en la lista, y al parar en vuelo el detalle del estado dice «Parado en vuelo» hasta que CTT confirme la vuelta. En la novedad de ayer tenéis adjuntos la guía en PDF y un vídeo de un minuto.',
     N'Administración,Almacén,Dirección'),
    ('Nuevo', N'Aplazar la fecha de entrega de un pedido que ya tiene picking',
     N'Al cambiar la fecha de entrega de un pedido con picking: si el envío todavía no ha salido, se guarda, se avisa y Almacén recibe la campana (como con «Retener»). Si ya lo tiene CTT, Nesto le pide a CTT la fecha nueva y lo cuenta («La fecha de entrega se ha cambiado también en CTT»); si CTT la rechaza, el pedido no se guarda y se ve el motivo. Si lo tiene otra agencia, no se llega a tiempo: Nesto lo dice y ofrece el botón «Mandar correo a logística», que escribe a logística con copia al vendedor para que lo intenten; la otra opción es que el cliente lo rechace.',
     NULL),
    ('Nuevo', N'Reparto propio: la etiqueta se saca al cerrar el bulto aunque el reparto sea otro día',
     N'Los pedidos de nuestra ruta con fecha de entrega futura ya no se quedan abiertos esperando: la etiqueta RP se imprime al cerrar el bulto, con la fecha de reparto, y el bulto queda etiquetado hasta ese día. En Agencias la «Fecha entrega» del envío RP es la fecha de reparto. La furgoneta solo carga los bultos del día: si se lee uno de otro día, la PDA lo rechaza y dice de qué día es. Al aplazar un pedido que ya tiene la etiqueta RP, la fecha del envío se mueve sola.',
     N'Administración,Almacén,Dirección'),
    ('Mejorado', N'Picking: la ruta 16 y la AT entregan el mismo día en que sale el picking',
     N'Hasta ahora el picking contaba que lo de nuestra ruta se entregaba al día siguiente, y por eso un cliente que cierra los lunes se quedaba fuera el viernes. Ahora cuenta que se entrega el mismo día del picking (el automático de las 9), así que ese pedido sale el viernes. Almacén prepara los pedidos de la 16 el mismo día en que sale el picking, no la víspera.',
     N'Almacén,Dirección,Administración'),
    ('Mejorado', N'Eventos mixtos: se cuentan asistentes, y el enlace de conexión va solo a las inscritas online',
     N'En la cabecera de inscritos de un evento, el contador «presencial · online · sin indicar» cuenta personas, no inscripciones (una inscripción de 2 asistentes cuenta 2), para preparar la sala y las conexiones. En los eventos mixtos, el recordatorio con el enlace de conexión solo lo reciben las inscritas online; las presenciales y las «sin indicar» reciben la dirección y la hora. El enlace de un evento online se coge de la primera dirección web que haya en el Lugar («Zoom: https://…» ya vale).',
     N'Administración,Tiendas,Dirección,Vendedores'),
    ('Mejorado', N'La campana: los avisos urgentes y los de Dirección llegan también a la PDA',
     N'Los avisos que nacen en Nesto y son urgentes (como la autorización de un reembolso) o van a Dirección o Informática llegan también como aviso en la PDA (Ariadna) de la misma persona, sin duplicarse en el buzón. Los avisos normales de Nesto no van a la PDA.',
     N'Dirección,Administración'),
    ('Nuevo', N'Cheque regalo (próxima campaña): copia al vendedor y carta en papel',
     N'Para la próxima campaña de cheques: el correo del cheque va con copia oculta al vendedor del cliente, y al facturar un pedido que genera cheque se imprime en el servidor una carta en papel con el mismo contenido que el correo (importe, hasta cuándo, cómo se usa y la imagen), para meterla en el paquete. Sale junto a la factura, en la impresora de documentos de quien factura. La guía del cheque regalo está actualizada.',
     N'Administración,Almacén,Dirección,Vendedores');

INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;
SELECT Id, Categoria, Titulo FROM dbo.Novedades WHERE Version = @version ORDER BY Id;
