/*
    Novedades de la versión 1.10.41.0 de Nesto (13/10/2026). Ariadna sigue en 2.5 (nueva compilación: marca «Puntual» en Entradas).
    Versión de la ClickOnce: 1.10.41.0 (pubxml 1.10.41.* en revisión 0).

    Tanda: NestoAPI (579 unir pedidos/MSDTC [publicado 9:23], domiciliaciones sin remesa, #606 «Por qué» del correo resumido y
    sin fechas de lo que espera, kits desmontables en la fecha prometida y en el modo de servicio, informe de kits, MTP fuera,
    sugerencia 507 «CTT no responde» + aviso de posible duplicado + sin reintento al modificar, producto inexistente 400,
    PutCCCs 400 (Laura), 580 tamaño en el listado de reposición, 566 «Servido el…, pendiente de facturar» (NestoApp#225),
    #544 un correo por cliente y destinatario y nada en festivos, #593 estadística de cheques + impulso en clientes para contactar,
    pronto pago en el carrito de la app y recibo con sus plazos habituales, 581 reposición puntual con recogida marcada por la
    tienda, 575 aviso de rutas propias al sacar el picking + registro del cambio de ruta, 582 contacto en estado 9 primero,
    copia oculta de los correos del cheque quitada)
    + Nesto (bucle de errores en Clientes › cuentas, «CTT no responde» al tramitar/anular/modificar, pestaña «Cheques regalo» en
    Rapports, reposición puntual y «El repartidor ya se la ha llevado», aviso de kits y de rutas al sacar el picking, 582 en
    Rapports, #490 4C.2 punto 2 completo)
    + Ariadna (marca «Puntual» en Entradas).

    ORDEN:
      1) Scripts sa (da igual antes o después de la API; todos idempotentes):
         - Issue606_prdInformeKitsQueSePuedenMontar_Desmontar.sql   (ALTER del SP del informe de kits)
         - Issue581_ReposicionesTraspasos_Recogida.sql              (FechaRecogida/UsuarioRecogida)
         - Issue575_RegistroCambiosRuta.sql                         (trigger del cambio de ruta → LogModificaciones)
         - Issue575_RutasPropiasActivasEmpresa3.sql                 (AT y 16 activas en la empresa 3)
         - Issue593_ChequesRegalo_DiasImpulsoLlamadas.sql           (OPCIONAL: sin él, 10 días)
      2) Publicar NestoAPI (Web.config del repo: ChequesRegalo:CopiaOculta vacío = sin copia oculta).
      3) Publicar la ClickOnce (Clean + Rebuild). Ariadna: workflows de Play interna y Windows (2.5, nueva compilación).
      4) DESPUÉS: ESTE script (idempotente: compara por Versión + Título).
      5) Scripts/OneShot_20261013_CerrarSugerencias_1_10_41_0.ps1: pone versión y perfiles a las sugerencias 507, 580, 581 y 582,
         y contesta en cada hilo como «Claude (asistente IA)».
      6) Re-adjuntar las guías: Guia cheque regalo.pdf a la 567; Guia reposiciones automaticas.pdf a la de la puntual de esta
         versión (sugerencia 581) y a las de reposiciones (552, 562).
      7) TNV 3.34.0 a producción cuando Carlos la haya probado en la pista interna.

    SE OMITE A PROPÓSITO:
      - Lo que va por las sugerencias de arriba (507, 580, 581, 582).
      - 579 (unir pedidos): arreglado y publicado esta mañana; Marta avisada en su hilo.
      - Producto inexistente 400, PutCCCs 400, domiciliaciones, MTP, registro del cambio de ruta, #490: internos o sin cambio visible.
      - La app de clientas (pronto pago, recibo) va por sus notas de Play; NestoApp (566, cheques) por sus issues.

    Ejecutar en SSMS contra NV como sa. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.41.0';
DECLARE @fecha DATE = '2026-10-13';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Cheques regalo de cada vendedor: generados, usados y pendientes',
     N'En Rapports, al desplegar «Crear Rapport», hay una pestaña «Cheques regalo» junto a «Clientes para contactar». Arriba, cuántos días quedan para usarlos y un resumen por vendedor (generados, usados y pendientes); cada vendedor ve los suyos, los jefes de ventas los de su equipo y dirección todos. Debajo, la lista de clientes con el cheque sin usar, los que caducan antes primero, con teléfonos, la factura que lo generó (botón «Factura» para ver el PDF) y botones para llamar o mandar un WhatsApp, que abren el rapport. Del 29 de octubre al 7 de noviembre, esos clientes salen los primeros en «Clientes para contactar». La guía del cheque regalo, actualizada, está adjunta a la novedad del cheque.',
     N'Vendedores,Administración'),
    ('Nuevo', N'Al sacar el picking, los pedidos que podrían ir por nuestra ruta',
     N'Al sacar el picking, si hay pedidos que van por agencia pero podrían ir por nuestra ruta (AT o 16), Nesto los enseña antes con la ruta propuesta y el motivo: de la empresa 3 con código postal de Madrid, el último pedido del cliente salió por nuestra ruta, o aparatos de 1.500 € o más en la empresa 1. Se marcan los que se quieren pasar, se puede elegir la otra ruta y «Cambiar la ruta de los marcados y sacar el picking». Nadie más ve esta propuesta: la decide almacén. Los cambios de ruta quedan apuntados (quién y cuándo).',
     N'Almacén'),
    ('Mejorado', N'Los kits que se pueden desmontar cuentan para la fecha de entrega',
     N'Si falta un producto en Algete pero está dentro de un kit que hay allí (por ejemplo, una caja de 70 guantes), la fecha de entrega que se le dice al cliente y el modo de servir que propone la plantilla cuentan con desmontarlo, en vez de esperar a la reposición de una tienda. El informe de kits del picking dice ahora cuántas cajas hay que desmontar (antes decía unidades) y no propone desmontar kits ya vendidos. Al sacar el picking, si hay kits que desmontar y no se ha sacado el informe, Nesto lo pregunta. Los productos fabricados (con materias primas) no se desmontan.',
     N'Almacén,Vendedores,Tiendas'),
    ('Mejorado', N'El «Por qué» de la fecha de entrega en el correo del pedido, más claro',
     N'En el correo del pedido, el motivo de la fecha de entrega a la agencia ya no repite uno a uno todos los productos: si todo tiene stock en Algete dice «Todo el pedido: con stock en Algete»; si falta alguno, solo se nombran los que van distintos y el resto va en una línea («El resto: …»). Con «Todo junto» o «Tras reponer de tiendas», lo que tiene stock ya no dice «puede salir el martes» si el pedido no sale hasta el jueves: solo lleva fecha lo que la marca.',
     N'Vendedores,Tiendas,Administración'),
    ('Mejorado', N'Un solo correo de facturas vencidas por cliente, y nunca en festivo',
     N'El aviso automático de facturas vencidas por transferencia manda un solo correo cuando varios contactos del mismo cliente tienen el mismo correo (antes salían dos a la vez), con la dirección de cada factura si son de varios centros. El resumen diario da el total por cliente. En festivo no se manda nada.',
     N'Administración'),
    ('Corregido', N'Clientes › cuentas bancarias: ya no se queda Nesto lleno de errores',
     N'Al cambiar a un cliente sin direcciones de entrega activas en la pantalla de cuentas bancarias, Nesto podía quedarse sacando el mismo error sin parar y no dejaba ni cerrarlo. Ya no pasa. Además, al guardar una cuenta incompleta, Nesto dice qué falta (por ejemplo, «falta la entidad, la oficina, el DC y el número de cuenta») en vez de un error sin explicación.',
     N'Administración');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;
