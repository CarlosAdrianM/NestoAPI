/*
    OneShot_20260909_FichasMirplay25.sql
    ------------------------------------
    Textos de tienda (nombre, descripcion corta y larga) de 25 referencias de la
    familia Mirplay que estaban publicadas en PrestaShop SIN foto y con la
    descripcion vacia (solo el nombre + la coletilla de sobre-pedido que inyecta
    el modulo).

    QUE HACE
      1. INSERTA una fila en PrestashopProductos por cada referencia, con
         Nombre / DescripcionBreve / Descripcion y VistoBueno = 1.
      2. ENCOLA las 25 en Nesto_sync (Tabla = 'Productos') para que el job de
         Hangfire 'sincronizar-productos' (cada 5 min, lotes de 50) las publique.

    POR QUE ES SEGURO EN PRECIOS
      PVP_IVA_Incluido se deja a NULL = modo derivado (PVP / 0,7 x IVA), que es
      exactamente lo que la tienda calcula hoy para estas 25 al no existir fila.
      ProductoDTO.ResolverPrecioPublicoFinal(null, true) devuelve null, o sea
      "no es un precio fijo": el VistoBueno NO cambia ningun precio, solo abre la
      puerta a que viajen los TEXTOS (ProductoDTO.CargarTextosTienda, #411).

    QUE NO HACE
      - No sube fotos: eso lo hace el equipo del servidor de PrestaShop
        (Scripts/NestoSync_PromptEquipoPrestashop_FotosMirplay.md).
      - No activa los productos en la tienda: los 25 estan hoy con active = 0 y
        los activa el equipo de PrestaShop cuando las fotos esten arriba.
      - No toca la coletilla de sobre-pedido: la pone el modulo (etiquetas
        dinamicas, NESTOSYNC_LABELS_ENABLED).

    ORDEN DE EJECUCION
      Se puede ejecutar ANTES de que suban las fotos: el producto sigue inactivo
      en la tienda, asi que el texto no se ve hasta que se active. Recomendado
      lanzarlo primero, para que el equipo de PrestaShop vea ya la descripcion
      cuando revise cada ficha.

    LAS 25 SON 25 MODELOS DISTINTOS
      Se ha excluido a proposito toda referencia que en Nesto tenga hermanas de
      color (Chloe 32, Oke 14, Loris 8, Check 4, y las parejas Ralph, Jacob, Jordan
      y Pol). Esas familias se trataran como producto padre y variantes, con la
      relacion guardada en Nesto, y hasta entonces no se publican para no crear en
      la tienda veinte fichas casi identicas compitiendo entre si.

    NOTA DE REGISTRO (09/09/2026)
      Al validar una version anterior de este script se encolaron por error 25 filas
      en Nesto_sync con Usuario = 'SEO Mirplay'. No llevaban textos (el INSERT en
      PrestashopProductos se denego por permisos), el job las drenó sin cambiar nada
      en PrestaShop y la cola quedo a cero. No afecta a esta ejecucion, pero si la
      consulta C del bloque 1 devolviera algo distinto de 0, retirarlas antes:
        DELETE FROM Nesto_sync WHERE Usuario = 'SEO Mirplay' AND Sincronizado IS NULL;

    COMO SE EJECUTA
      1. Lanzar el BLOQUE 1 (comprobaciones) y revisar las tres salidas.
      2. Quitar el comentario del BLOQUE 2 (empieza en "QUITAR ESTE COMENTARIO")
         y ejecutarlo. Debe decir Encoladas = 25.
      3. Quitar el comentario de COMMIT TRAN y ejecutarlo. Si algo no cuadra,
         ROLLBACK TRAN.
      4. Seguir el drenaje con el BLOQUE 3 hasta Pendientes = 0.

    NO HAY QUE HACER NADA MAS EN NESTO
      El VistoBueno va puesto a 1 en el propio INSERT, asi que nadie tiene que
      aprobarlo a mano en la pantalla, y el encolado tambien lo hace este script.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
USE NV;
GO

-- =============================================================================
-- 1) COMPROBACIONES. Las 25 deben salir aqui con Filas = 0 y Estado >= 0.
-- =============================================================================
PRINT '--- A) Estado de partida de las 25 referencias ---';
SELECT RTRIM(p.Número) AS Referencia, RTRIM(p.Nombre) AS NombreFicha, p.Estado, p.PVP,
       (SELECT COUNT(*) FROM PrestashopProductos pp
         WHERE pp.Empresa = p.Empresa AND pp.Número = p.Número) AS FilasPrestashop
FROM Productos p
WHERE p.Empresa = '1' AND RTRIM(p.Número) IN (
    '45735',
    '45732',
    '45729',
    '45723',
    '45821',
    '45812',
    '45820',
    '45811',
    '45817',
    '45822',
    '45818',
    '44497',
    '45800',
    '45750',
    '45788',
    '45799',
    '45854',
    '45849',
    '45851',
    '45749',
    '45711',
    '45710',
    '45717',
    '45713',
    '44367')
ORDER BY p.Número;
--  Si alguna sale con FilasPrestashop = 1, el INSERT de abajo fallaria por clave
--  duplicada: convertir esa fila concreta en UPDATE antes de seguir.

PRINT '--- B) Cola de sincronizacion (deberia estar cerca de cero) ---';
SELECT Tabla, COUNT(*) AS Pendientes FROM Nesto_sync
WHERE Sincronizado IS NULL GROUP BY Tabla;

PRINT '--- C) Restos de la cola encolada por error (ver NOTA DE REGISTRO) ---';
SELECT COUNT(*) AS PendientesSEOMirplay FROM Nesto_sync
WHERE Usuario = 'SEO Mirplay' AND Sincronizado IS NULL;
--  El 09/09 a las 19:00 esto devolvia 0. Si devuelve otra cosa, borrarlas antes.
GO

-- =============================================================================
-- 2) LOS TEXTOS. Revisados uno a uno contra la ficha del fabricante.
--    Marcado permitido: <p> <h2> <h3> <ul> <li> <strong> <em>. Sin atributos,
--    sin <h1>, sin <style> y sin <script>: nada que pueda tocar el CSS ni las
--    cabeceras de la pagina de PrestaShop.
-- =============================================================================
/*  QUITAR ESTE COMENTARIO Y EJECUTAR CUANDO EL BLOQUE 1 CUADRE

BEGIN TRAN;

-- 45735  LAVACABEZAS OLIVER  (420.0000 EUR)  ->  https://mirplaysalon.com/producto/lavacabezas-oliver
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45735',
    N'Lavacabezas Oliver de polipropileno - Mirplay',
    N'Lavacabezas Oliver de Mirplay, el modelo básico de la gama: estructura y cerámica de polipropileno negro, asiento ergonómico, instalación sencilla y mantenimiento fácil. La opción de entrada para montar una zona de lavado funcional en peluquerías y barberías.',
    N'<p>El <strong>lavacabezas Oliver de Mirplay</strong> es el modelo de entrada de la gama: la manera más directa de montar una zona de lavado que funcione, con una <strong>estructura y cerámica de polipropileno negro</strong> que simplifica tanto la instalación como el mantenimiento posterior.</p>
<h2>Características del lavacabezas Oliver</h2>
<ul>
<li>Modelo básico, pensado para empezar.</li>
<li>Estructura y cerámica de polipropileno negro.</li>
<li>Asiento ergonómico para mayor comodidad.</li>
<li>Instalación sencilla y mantenimiento fácil.</li>
</ul>
<h3>Para qué salón está pensado</h3>
<p>Es el <em>lavacabezas económico</em> con el que suelen abrir muchos salones y barberías nuevas, y también la opción habitual como segundo puesto de lavado cuando el volumen crece y hace falta descongestionar el principal sin duplicar la inversión.</p>
<h3>Materiales y mantenimiento</h3>
<p>El polipropileno es un material ligero, que no se oxida y que aguanta bien los productos químicos del salón. Al ser una pieza única, no acumula suciedad en juntas ni requiere los cuidados de una cerámica tradicional, lo que reduce el mantenimiento de la zona de lavado a un simple repaso diario.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, distribuido por Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45732  LAVACABEZAS LEE  (90.0000 EUR)  ->  https://mirplaysalon.com/producto/lavacabezas-portatil-lee
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45732',
    N'Lavacabezas portátil Lee con depósito de 10 litros - Mirplay',
    N'Lavacabezas portátil Lee de Mirplay, con cerámica de polipropileno ajustable en altura e inclinación y depósito de 10 litros. Ligero y fácil de mover: la solución para servicios a domicilio, residencias y espacios reducidos donde no hay toma de agua.',
    N'<p>El <strong>lavacabezas portátil Lee de Mirplay</strong> resuelve el lavado allí donde no hay una instalación fija. Con su <strong>depósito de 10 litros</strong> trabaja de forma autónoma, sin necesidad de toma de agua, y se traslada con facilidad de una estancia a otra.</p>
<h2>Características del lavacabezas portátil Lee</h2>
<ul>
<li>Formato portátil, para domicilios y espacios pequeños.</li>
<li>Cerámica de polipropileno ajustable en altura e inclinación.</li>
<li>Depósito de 10 litros para trabajar con autonomía.</li>
<li>Ligero, versátil y fácil de usar.</li>
</ul>
<h3>Para qué profesional está pensado</h3>
<p>Es la herramienta del profesional que trabaja fuera del salón: <em>peluquería a domicilio</em>, residencias de mayores, hospitales, rodajes y eventos. También cubre al salón pequeño que no puede permitirse un puesto de lavado fijo, o al que necesita un apoyo puntual en días de mucha carga.</p>
<h3>Ajuste y comodidad</h3>
<p>La cerámica se regula en altura e inclinación, que es justo lo que hace viable un lavado cómodo cuando el cliente está sentado en una silla cualquiera y no en un sillón de peluquería. Es la diferencia entre un apaño y un servicio profesional.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45729  LAVACABEZAS JOKER  (87.0000 EUR)  ->  https://mirplaysalon.com/producto/reposapies-joker
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45729',
    N'Reposapiés Joker para lavacabezas - Mirplay',
    N'Reposapiés Joker de Mirplay para completar el lavacabezas, en poli-piel de PVC retardante al fuego y espuma de alta densidad de recuperación rápida. La forma económica de mejorar la postura del cliente durante el lavado.',
    N'<p>El <strong>reposapiés Joker de Mirplay</strong> completa el lavacabezas por muy poco dinero. Apoyar las piernas cambia por completo la postura durante el lavado, sobre todo en los servicios largos en los que el cliente pasa varios minutos tumbado.</p>
<h2>Características del reposapiés Joker</h2>
<ul>
<li>Tapizado en poli-piel de PVC, retardante al fuego.</li>
<li>Espuma de alta densidad de recuperación rápida.</li>
<li>Opción económica y funcional.</li>
</ul>
<h3>Para qué zona de lavado está pensado</h3>
<p>Para los lavacabezas que no traen reposapiés incorporado, que son la mayoría de los modelos de acceso. Es la mejora más barata que se le puede hacer a una <em>zona de lavado</em> ya montada.</p>
<h3>Por qué la espuma de recuperación rápida</h3>
<p>Es una pieza que recibe todo el peso de las piernas decenas de veces al día. Una espuma de alta densidad mantiene la forma en lugar de quedarse hundida a los pocos meses.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45723  LAVACABEZAS BONNIE  (33.0000 EUR)  ->  https://mirplaysalon.com/producto/cojin-alzador-bonnie
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45723',
    N'Cojín alzador Bonnie para lavacabezas - Mirplay',
    N'Cojín alzador Bonnie de Mirplay para la zona de lavado, en poli-piel de PVC retardante al fuego y espuma de alta densidad. Mejora la postura y la comodidad del cliente durante el lavado, sobre todo en cuellos que no llegan bien a la pila.',
    N'<p>El <strong>cojín alzador Bonnie de Mirplay</strong> resuelve un problema cotidiano de la zona de lavado: no todos los clientes encajan igual en la pila. Un alzador corrige la altura y evita que el cuello quede forzado durante todo el lavado.</p>
<h2>Características del cojín alzador Bonnie</h2>
<ul>
<li>Tapizado en poli-piel de PVC, retardante al fuego.</li>
<li>Espuma de alta densidad, para un apoyo extra.</li>
<li>Accesorio pensado para la zona de lavado.</li>
</ul>
<h3>Cuándo se nota de verdad</h3>
<p>Con personas de baja estatura, con clientes mayores y en lavados largos. Es también lo primero que se prueba cuando alguien se queja de molestias de cuello en el <em>lavacabezas</em>: muchas veces no es la pila, es la altura.</p>
<h3>Mantenimiento</h3>
<p>La poli-piel se limpia con un paño húmedo entre clientes, que es lo que hace falta en un accesorio que convive con agua y champú todo el día.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45821  SILLON DE BARBERO PRINCE  (878.0000 EUR)  ->  https://mirplaysalon.com/producto/sillon-de-barbero-prince
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45821',
    N'Sillón de barbero Prince de aluminio inyectado - Mirplay',
    N'Sillón de barbero Prince de Mirplay, con estructura de aluminio inyectado de alta durabilidad, respaldo reclinable y reposapiés reclinable. Tapizado en polipiel de PVC negra retardante al fuego. Diseño clásico y construcción robusta para barberías de alta rotación.',
    N'<p>El <strong>sillón de barbero Prince de Mirplay</strong> combina un diseño clásico con una construcción pensada para durar. Su <strong>estructura de aluminio inyectado</strong> es el argumento principal: aporta rigidez y resistencia sin el peso de las estructuras macizas tradicionales.</p>
<h2>Características del sillón de barbero Prince</h2>
<ul>
<li>Estructura de aluminio inyectado de alta durabilidad.</li>
<li>Respaldo reclinable para mayor comodidad.</li>
<li>Reposapiés reclinable para una postura más confortable.</li>
<li>Tapizado en polipiel de PVC negra, retardante al fuego.</li>
</ul>
<h3>Para qué barbería está pensado</h3>
<p>Es el <em>sillón de barbero profesional</em> para locales de alta rotación, donde el mueble encadena servicios de la mañana a la noche. En ese escenario la elección del material de la estructura deja de ser un detalle y pasa a determinar cuántos años aguanta el sillón en condiciones.</p>
<h3>Comodidad en servicios largos</h3>
<p>Respaldo y reposapiés reclinables permiten llevar al cliente a la postura de afeitado sin que tenga que recolocarse. En barberías que trabajan la barba y el ritual de toalla caliente, esa reclinación es lo que separa un servicio cómodo de uno que se hace largo.</p>
<h3>Materiales y mantenimiento</h3>
<p>El tapizado en polipiel de PVC negra retardante al fuego es fácil de mantener y disimula el desgaste, algo a tener en cuenta en <em>mobiliario de barbería</em> que se usa a diario.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45812  SILLON DE BARBERO BART  (741.0000 EUR)  ->  https://mirplaysalon.com/producto/sillon-de-barbero-bart
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45812',
    N'Sillón de barbero Bart vintage con soporte de toalla - Mirplay',
    N'Sillón de barbero Bart de Mirplay: estilo vintage con acabados cromados y laterales blancos, respaldo y reposapiés reclinables, reposacabezas regulable y soporte para toalla integrado. Pensado para barberías que buscan una pieza con presencia y uso profesional diario.',
    N'<p>El <strong>sillón de barbero Bart de Mirplay</strong> lleva la esencia clásica de la barbería a una estética actual. El tapizado negro con <strong>laterales blancos y detalles cromados</strong> le da una presencia que ordena visualmente todo el local, y el equipamiento está a la altura del uso profesional diario.</p>
<h2>Características del sillón de barbero Bart</h2>
<ul>
<li>Respaldo reclinable para mayor comodidad.</li>
<li>Reposapiés reversible e inclinable.</li>
<li>Reposacabezas regulable.</li>
<li>Soporte para toalla integrado.</li>
<li>Estilo vintage con acabados cromados y laterales blancos.</li>
</ul>
<h3>El detalle que lo diferencia</h3>
<p>El <strong>soporte para toalla integrado</strong> no es un adorno: en una barbería que trabaja el afeitado tradicional, tener la toalla en el propio sillón evita el paseo al carro en mitad del servicio y mantiene el ritmo del ritual. Es el tipo de detalle que solo se aprecia después de usarlo unas semanas.</p>
<h3>Para qué barbería está pensado</h3>
<p>Para el local que quiere un <em>sillón de barbería vintage</em> reconocible, sin renunciar a la reclinación y los ajustes de un sillón moderno. Funciona igual de bien como puesto único de una barbería pequeña que en fila dentro de un local con varios puestos.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45820  SILLON DE BARBERO KARL  (641.0000 EUR)  ->  https://mirplaysalon.com/producto/sillon-de-barbero-karl
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45820',
    N'Sillón de barbero Karl clásico y económico - Mirplay',
    N'Sillón de barbero Karl de Mirplay: diseño tradicional atemporal, estructura robusta para uso profesional y respaldo reclinable. La opción económica para equipar una barbería con un clásico del sector sin renunciar a funcionalidad.',
    N'<p>El <strong>sillón de barbero Karl de Mirplay</strong> es un clásico del mobiliario de barbería. Su planteamiento es directo: <strong>diseño tradicional, estructura robusta y precio contenido</strong>, sin florituras que encarezcan el mueble y sin renunciar a lo que de verdad se usa cada día.</p>
<h2>Características del sillón de barbero Karl</h2>
<ul>
<li>Diseño clásico y atemporal, de estilo tradicional.</li>
<li>Estructura robusta para uso profesional.</li>
<li>Respaldo reclinable para mayor confort.</li>
<li>Opción económica sin renunciar a la funcionalidad.</li>
</ul>
<h3>Para qué barbería está pensado</h3>
<p>Es la elección habitual cuando hay que <em>equipar una barbería</em> entera de golpe y el número de puestos multiplica cualquier diferencia de precio. También funciona como puesto de refuerzo en locales que crecen y necesitan sumar un sillón más manteniendo la estética tradicional del resto.</p>
<h3>Lo que aporta el respaldo reclinable</h3>
<p>Aunque sea el modelo de acceso de la gama, mantiene la reclinación del respaldo, que es lo que permite trabajar el afeitado y el arreglo de barba con el cliente en una postura cómoda. Sin ella, un sillón de barbero se queda en una silla de corte.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45811  SILLON DE BARBERO ASHER  (579.0000 EUR)  ->  https://mirplaysalon.com/producto/sillon-de-barbero-asher
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45811',
    N'Sillón de barbero Asher hidráulico - Mirplay',
    N'Sillón de barbero Asher de Mirplay, regulable en altura mediante sistema hidráulico, con reposacabezas extraíble y ajustable y reposapiés reversible e inclinable de aluminio. Tapizado en polipiel negra retardante al fuego y superficie acolchada. Un clásico versátil a precio competitivo.',
    N'<p>El <strong>sillón de barbero Asher de Mirplay</strong> es de esos modelos que no fallan en ninguna barbería: diseño atemporal, los ajustes que se usan a diario y una relación calidad-precio que lo hace fácil de justificar cuando hay que comprar varios puestos.</p>
<h2>Características del sillón de barbero Asher</h2>
<ul>
<li>Regulable en altura mediante sistema hidráulico.</li>
<li>Reposacabezas extraíble y ajustable.</li>
<li>Reposapiés reversible e inclinable de aluminio.</li>
<li>Superficie acolchada para mayor confort.</li>
<li>Tapizado en polipiel negra, fácil de mantener y retardante al fuego.</li>
</ul>
<h3>Por qué importa la regulación hidráulica</h3>
<p>Un <em>sillón de barbero hidráulico</em> no se ajusta para el cliente, se ajusta para el barbero: subir o bajar el puesto hasta trabajar con los hombros relajados es lo que evita las molestias de espalda al cabo de una jornada de ocho o diez servicios. Con clientes de alturas muy distintas, ese recorrido se usa constantemente.</p>
<h3>Para qué barbería está pensado</h3>
<p>Para el uso profesional diario, tanto en barberías que arrancan como en las que renuevan puestos. El reposacabezas extraíble permite alternar corte y afeitado sin cambiar al cliente de sitio.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45817  SILLON DE BARBERO DAVE  (533.0000 EUR)  ->  https://mirplaysalon.com/producto/sillon-de-barbero-dave
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45817',
    N'Sillón de barbero Dave retro con acolchado en rombos - Mirplay',
    N'Sillón de barbero Dave de Mirplay: estilo retro con tapizado negro acolchado en rombos, regulación hidráulica en altura, respaldo reclinable, reposacabezas extraíble y reposapiés reversible de aluminio. Elegancia y resistencia a un precio ajustado.',
    N'<p>El <strong>sillón de barbero Dave de Mirplay</strong> tiene un carácter marcadamente retro que se apoya en un detalle concreto: el <strong>acolchado en rombos</strong> del tapizado negro. Es lo que da al puesto ese aire de barbería de toda la vida, sobre una base técnica actual.</p>
<h2>Características del sillón de barbero Dave</h2>
<ul>
<li>Regulable en altura mediante sistema hidráulico.</li>
<li>Respaldo reclinable para mayor comodidad.</li>
<li>Reposacabezas extraíble y ajustable.</li>
<li>Reposapiés reversible y reclinable de aluminio.</li>
<li>Tapizado negro con acolchado en rombos.</li>
</ul>
<h3>Para qué barbería está pensado</h3>
<p>Para la barbería que busca un <em>sillón de barbero retro</em> con presencia pero tiene el presupuesto medido. Reúne la reclinación del respaldo, la regulación hidráulica y el reposacabezas ajustable, es decir, todo lo necesario para trabajar corte, barba y afeitado en el mismo puesto.</p>
<h3>Materiales y mantenimiento</h3>
<p>El reposapiés de aluminio aguanta bien el apoyo continuo y no se oxida con la humedad de las toallas. El tapizado se repasa con un paño húmedo entre clientes, sin productos agresivos.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45822  SILLON DE BARBERO SILVIA B  (499.0000 EUR)  ->  https://mirplaysalon.com/producto/sillon-de-barbero-silvia-b
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45822',
    N'Sillón de barbero Silvia B con reposapiés antivuelco - Mirplay',
    N'Sillón de barbero Silvia B de Mirplay, con asiento ergonómico reclinable, reposacabezas regulable y extraíble y reposapiés con soporte antivuelco. Estructura reforzada, ligera y resistente, tapizada en poli-piel de PVC negra. Práctico y económico para empezar o renovar equipamiento.',
    N'<p>El <strong>sillón de barbero Silvia B de Mirplay</strong> es una opción práctica y económica para quien monta su primera barbería o quiere renovar puestos sin disparar la inversión. Su diseño moderno en negro encaja en casi cualquier local.</p>
<h2>Características del sillón de barbero Silvia B</h2>
<ul>
<li>Asiento ergonómico reclinable.</li>
<li>Reposacabezas regulable y extraíble.</li>
<li>Reposapiés con soporte antivuelco.</li>
<li>Estructura reforzada, ligera y resistente.</li>
<li>Tapizado en poli-piel de PVC en color negro.</li>
</ul>
<h3>El soporte antivuelco</h3>
<p>El <strong>reposapiés con soporte antivuelco</strong> es una medida de seguridad que se agradece: es habitual que el cliente cargue todo su peso sobre el reposapiés al sentarse o levantarse, y ese apoyo evita el balanceo del conjunto justo en el momento de mayor riesgo.</p>
<h3>Para qué barbería está pensado</h3>
<p>Para negocios que empiezan y para barberías que necesitan un <em>sillón de barbero económico</em> como puesto adicional. Al ser una estructura ligera, también es más fácil de recolocar si el local cambia de distribución.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45818  SILLON DE BARBERO JOEY 1  (430.0000 EUR)  ->  https://mirplaysalon.com/producto/sillon-de-barbero-joey-1
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45818',
    N'Sillón de barbero Joey 1 hidráulico acolchado - Mirplay',
    N'Sillón de barbero Joey 1 de Mirplay, con regulación hidráulica en altura, respaldo reclinable, reposacabezas extraíble y ajustable y reposapiés con soporte antivuelco. Tapizado negro acolchado con líneas en poli-piel de PVC retardante al fuego. Buena relación calidad-precio.',
    N'<p>El <strong>sillón de barbero Joey 1 de Mirplay</strong> demuestra que un modelo económico no tiene por qué renunciar a comodidad ni a acabado. Su <strong>tapizado negro acolchado con líneas</strong> le da un aire cuidado, y el equipamiento cubre lo que se usa en cada servicio.</p>
<h2>Características del sillón de barbero Joey 1</h2>
<ul>
<li>Regulable en altura mediante sistema hidráulico.</li>
<li>Respaldo reclinable para mayor comodidad.</li>
<li>Reposacabezas extraíble y ajustable.</li>
<li>Reposapiés con soporte antivuelco.</li>
<li>Tapizado en poli-piel de PVC negra, retardante al fuego.</li>
</ul>
<h3>Para qué barbería está pensado</h3>
<p>Para barberías que buscan <em>funcionalidad a buen precio</em> en el uso diario: la combinación de bomba hidráulica, respaldo reclinable y reposacabezas ajustable permite trabajar corte y barba en el mismo puesto, que es lo que se le pide a un sillón de barbero de trabajo.</p>
<h3>Materiales y mantenimiento</h3>
<p>La poli-piel de PVC retardante al fuego es el estándar del <em>mobiliario de barbería</em> profesional: resiste el uso continuo, se limpia rápido y cumple con los requisitos de seguridad de un local abierto al público.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 44497  SILLON ALYA B  (491.0000 EUR)  ->  https://mirplaysalon.com/producto/sillon-de-corte-alya-b
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '44497',
    N'Sillón de corte Alya B con respaldo de madera natural - Mirplay',
    N'Sillón de corte Alya B de la colección Signature de Mirplay: respaldo en chapa de madera natural, tapizado en polipiel negra de alta calidad, curvatura ergonómica y bomba hidráulica ajustable en altura. Con distintas opciones de base para personalizarlo según el estilo del salón.',
    N'<p>El <strong>sillón de corte Alya B de Mirplay</strong> pertenece a la colección Signature, la gama de piezas de diseño de la marca. Su elemento distintivo es el <strong>respaldo en chapa de madera natural</strong>, poco habitual en un sillón de peluquería y suficiente por sí solo para marcar el carácter de todo el salón.</p>
<h2>Características del sillón de corte Alya B</h2>
<ul>
<li>Respaldo con chapa de madera natural.</li>
<li>Bomba hidráulica ajustable en altura.</li>
<li>Tapizado en polipiel negra de alta calidad.</li>
<li>Curvatura ergonómica para mayor comodidad durante el servicio.</li>
<li>Diferentes opciones de base para personalizar el sillón.</li>
</ul>
<h3>Para qué salón está pensado</h3>
<p>Para peluquerías que compiten por diseño y quieren que el <em>mobiliario de peluquería</em> forme parte del relato de la marca. La madera aporta calidez a un local dominado por el negro y el metal, y las distintas bases permiten ajustar la pieza al estilo del resto del salón.</p>
<h3>Comodidad y ergonomía</h3>
<p>La curvatura del asiento y el respaldo acompaña la postura del cliente durante servicios largos, como coloraciones o mechas, en los que se pasa mucho tiempo sentado. La bomba hidráulica ajusta la altura para que el profesional trabaje cómodo.</p>
<p>Colección Signature de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45800  SILLON HORACE  (288.0000 EUR)  ->  https://mirplaysalon.com/producto/sillon-de-corte-horace
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45800',
    N'Sillón de corte Horace con capitoné y base cromada - Mirplay',
    N'Sillón de corte Horace de Mirplay, con brazos y respaldo en capitoné, estructura de madera y base cuadrada cromada. Espuma de alta densidad de recuperación rápida y tapizado en poli-piel de PVC negra retardante al fuego. Elegancia clásica para salones con estilo.',
    N'<p>El <strong>sillón de corte Horace de Mirplay</strong> apuesta por el detalle clásico: el <strong>capitoné en brazos y respaldo</strong>, sobre una estructura de madera y una base cuadrada cromada que le da estabilidad y una presencia más contemporánea.</p>
<h2>Características del sillón de corte Horace</h2>
<ul>
<li>Tapizado en poli-piel de PVC negra, retardante al fuego.</li>
<li>Espuma de alta densidad de recuperación rápida.</li>
<li>Brazos y respaldo con capitoné.</li>
<li>Estructura de madera.</li>
<li>Base cuadrada cromada, estable y elegante.</li>
</ul>
<h3>Por qué importa la espuma de alta densidad</h3>
<p>La <strong>recuperación rápida</strong> de la espuma es lo que mantiene la forma del asiento cuando el sillón encadena clientes durante todo el día. En los rellenos de baja densidad, el asiento se hunde en pocos meses y el sillón envejece mal a la vista, aunque el tapizado siga intacto.</p>
<h3>Para qué salón está pensado</h3>
<p>Para peluquerías que buscan un <em>sillón de corte elegante</em> con aire clásico. El capitoné y el cromado combinan bien con espejos de marco y tocadores de línea tradicional.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45750  SILLON BERTIE  (277.0000 EUR)  ->  https://mirplaysalon.com/producto/sillon-de-corte-bertie
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45750',
    N'Sillón de corte Bertie de líneas redondeadas - Mirplay',
    N'Sillón de corte Bertie de Mirplay, con líneas redondeadas en respaldo y asiento, interior de PU inyectado sobre estructura metálica y base redonda cromada. Tapizado en poli-piel de PVC negra de alta calidad, retardante al fuego. Diseño actual y duradero.',
    N'<p>El <strong>sillón de corte Bertie de Mirplay</strong> se reconoce por sus <strong>líneas redondeadas</strong> en respaldo y asiento, un diseño amable que suaviza el conjunto del salón y encaja bien tanto en peluquerías de estética moderna como en locales más neutros.</p>
<h2>Características del sillón de corte Bertie</h2>
<ul>
<li>Diseño de líneas redondeadas, moderno y elegante.</li>
<li>Interior de PU inyectado con estructura metálica.</li>
<li>Base redonda cromada, estable y resistente.</li>
<li>Tapizado en poli-piel de PVC negra de alta calidad, retardante al fuego.</li>
</ul>
<h3>Cómo está construido</h3>
<p>El <strong>PU inyectado sobre estructura metálica</strong> es una construcción pensada para durar: el metal sostiene y el poliuretano da la forma y el confort, sin las costuras y rellenos que suelen ser el punto débil de un sillón de peluquería con muchos años de uso.</p>
<h3>Para qué salón está pensado</h3>
<p>Para peluquerías que quieren un <em>sillón de corte</em> de aspecto actual y mantenimiento sencillo. La base redonda cromada ocupa poco y facilita el paso alrededor del puesto en salones con pasillos estrechos.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45788  SILLON CINDY  (235.0000 EUR)  ->  https://mirplaysalon.com/producto/sillon-de-corte-cindy
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45788',
    N'Sillón de corte Cindy con bomba hidráulica y freno - Mirplay',
    N'Sillón de corte Cindy de Mirplay, con bomba hidráulica con freno regulable en altura, asiento ergonómico de una sola pieza y base redonda de acero cromado. Respaldo de líneas redondeadas. La opción accesible para quien monta su primer salón sin renunciar al acabado.',
    N'<p>El <strong>sillón de corte Cindy de Mirplay</strong> está pensado para quien se inicia en el sector: precio accesible, comodidad real y un diseño cuidado que encaja en cualquier salón sin desentonar con el resto del mobiliario.</p>
<h2>Características del sillón de corte Cindy</h2>
<ul>
<li>Bomba hidráulica con freno, regulable en altura.</li>
<li>Base redonda de acero cromado, estable y resistente.</li>
<li>Asiento ergonómico de una sola pieza.</li>
<li>Respaldo de líneas redondeadas, de estética moderna.</li>
</ul>
<h3>Qué aporta el freno de la bomba</h3>
<p>La bomba hidráulica <strong>con freno</strong> es la diferencia entre un sillón que gira solo y uno que se queda donde lo dejas. En un corte de precisión, que el asiento no se mueva al apoyarse el cliente ahorra correcciones constantes.</p>
<h3>Para qué salón está pensado</h3>
<p>Para peluquerías que abren, y para las que necesitan sumar un <em>sillón de corte económico</em> a los puestos que ya tienen. El asiento de una sola pieza no tiene costuras donde se acumule el tinte, que es por donde envejecen los sillones baratos.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45799  SILLON GAT  (172.0000 EUR)  ->  https://mirplaysalon.com/producto/sillon-de-corte-infantil-gat
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45799',
    N'Sillón de corte infantil Gat con cinturón de seguridad - Mirplay',
    N'Sillón de corte infantil Gat de Mirplay, con pistón de gas ajustable en altura (15 cm), cinturón de seguridad ajustable y reposapiés de metal cromado. Tapizado en poli-piel de PVC de alta calidad retardante al fuego. La opción económica para atender a los más pequeños.',
    N'<p>El <strong>sillón de corte infantil Gat de Mirplay</strong> convierte el corte de pelo de los más pequeños en una experiencia cómoda y segura, sin obligar al salón a hacer una gran inversión para atender a ese público.</p>
<h2>Características del sillón de corte infantil Gat</h2>
<ul>
<li>Pistón de gas ajustable en altura, con 15 cm de recorrido.</li>
<li>Cinturón de seguridad ajustable.</li>
<li>Reposapiés de metal cromado.</li>
<li>Tapizado en poli-piel de PVC de alta calidad, retardante al fuego.</li>
</ul>
<h3>Seguridad para el niño y tranquilidad para el profesional</h3>
<p>El <strong>cinturón de seguridad ajustable</strong> es la pieza clave: mantiene al niño sentado en la posición correcta y reduce los movimientos bruscos justo cuando se trabaja con tijeras cerca de la cara y las orejas. Es lo que permite al profesional cortar con la atención puesta en el pelo.</p>
<h3>Para qué salón está pensado</h3>
<p>Para peluquerías familiares y barberías de barrio que atienden a niños con regularidad y quieren un <em>sillón infantil para peluquería</em> específico en lugar de recurrir a alzadores improvisados sobre el sillón de adulto.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45854  TOCADOR BOBBIE B  (184.0000 EUR)  ->  https://mirplaysalon.com/producto/tocador-bobbie-b
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45854',
    N'Tocador Bobbie B de melamina a doble cara - Mirplay',
    N'Tocador Bobbie B de Mirplay, estación de trabajo en aglomerado de alta densidad con melamina a doble cara y bordes redondeados. Superficie resistente y fácil de limpiar, práctica y económica como tocador de barbería o de peluquería.',
    N'<p>El <strong>tocador Bobbie B de Mirplay</strong> es una estación de trabajo directa y sin complicaciones: <strong>aglomerado de alta densidad con melamina a doble cara</strong>, una superficie que aguanta el uso profesional y se limpia en segundos.</p>
<h2>Características del tocador Bobbie B</h2>
<ul>
<li>Melamina a doble cara, fácil de limpiar.</li>
<li>Aglomerado de alta densidad, resistente.</li>
<li>Bordes redondeados, más seguridad y mejor acabado.</li>
</ul>
<h3>Por qué la melamina a doble cara</h3>
<p>En un <em>tocador de peluquería</em> el canto y la cara vista sufren el roce continuo de secadores, planchas y productos. El acabado por las dos caras evita que el tablero se deteriore por detrás, que es lo que ocurre en los muebles recubiertos solo por la cara visible cuando se colocan en isla o de espaldas a un espejo.</p>
<h3>Para qué salón está pensado</h3>
<p>Para barberías y peluquerías que necesitan equipar varios puestos con un <em>tocador de barbería</em> funcional y de precio contenido. Los bordes redondeados reducen los golpes en pasillos estrechos.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45849  TABURETE IAN  (92.0000 EUR)  ->  https://mirplaysalon.com/producto/taburete-ian
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45849',
    N'Taburete de sillín Ian con ruedas antipelo - Mirplay',
    N'Taburete de sillín Ian de Mirplay, con pistón de gas para ajuste de altura, base de aluminio y ruedas de silicona antipelo. Tapizado en poli-piel de PVC negra retardante al fuego. Pensado para trabajar con una postura más eficiente en peluquerías y barberías.',
    N'<p>El <strong>taburete de sillín Ian de Mirplay</strong> está pensado para lo que más se descuida al equipar un salón: la postura del profesional. El <strong>asiento tipo sillín</strong> abre el ángulo de la cadera y ayuda a mantener la espalda erguida durante toda la jornada.</p>
<h2>Características del taburete Ian</h2>
<ul>
<li>Taburete de sillín, cómodo y práctico para trabajar.</li>
<li>Pistón de gas para un ajuste de altura suave y preciso.</li>
<li>Base de aluminio, estable y duradera.</li>
<li>Ruedas de silicona antipelo para un desplazamiento fluido.</li>
<li>Tapizado en poli-piel de PVC negra, retardante al fuego.</li>
</ul>
<h3>Las ruedas antipelo, un detalle que se nota</h3>
<p>Las <strong>ruedas de silicona antipelo</strong> son la diferencia entre un taburete que rueda y uno que se atasca. En una peluquería el suelo acumula pelo cortado constantemente, y las ruedas convencionales lo enrollan en el eje hasta que dejan de girar. Es el motivo por el que muchos taburetes acaban arrastrándose en lugar de rodar.</p>
<h3>Para qué profesional está pensado</h3>
<p>Para peluqueros, barberos y esteticistas que trabajan sentados buena parte del día. Un <em>taburete de peluquería ergonómico</em> con regulación de altura permite adaptar el puesto a cada servicio en lugar de forzar la espalda.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45851  TABURETE NIL  (82.0000 EUR)  ->  https://mirplaysalon.com/producto/taburete-nil
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45851',
    N'Taburete Nil con ruedas de silicona - Mirplay',
    N'Taburete profesional Nil de Mirplay, de diseño redondeado, con pistón de gas regulable en altura, base en estrella y ruedas de silicona. Tapizado en poli-piel de PVC negra retardante al fuego. Cómodo y resistente para el trabajo diario.',
    N'<p>El <strong>taburete Nil de Mirplay</strong> es el taburete de trabajo de toda la vida, resuelto con un <strong>diseño redondeado</strong> que resulta más cómodo en jornadas largas que los asientos planos y rígidos.</p>
<h2>Características del taburete Nil</h2>
<ul>
<li>Tapizado en poli-piel de PVC negra, retardante al fuego.</li>
<li>Pistón de gas, regulable en altura.</li>
<li>Base en estrella, estable y resistente.</li>
<li>Ruedas de silicona para una movilidad suave.</li>
</ul>
<h3>Las ruedas de silicona</h3>
<p>En una peluquería el suelo acumula pelo cortado toda la jornada. Las ruedas de silicona se desplazan sin enredarlo en el eje, que es lo que acaba dejando clavado a cualquier <em>taburete de peluquería</em> convencional.</p>
<h3>Para qué profesional está pensado</h3>
<p>Para quien trabaja sentado buena parte del día y prefiere un asiento clásico al formato de sillín. Regular la altura en cada servicio es lo que evita acabar la jornada con la espalda cargada.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45749  SILLON ARI  (63.5000 EUR)  ->  https://mirplaysalon.com/producto/bandeja-ajustable-ari
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45749',
    N'Bandeja auxiliar ajustable Ari para sillón - Mirplay',
    N'Bandeja ajustable Ari de Mirplay, en aluminio resistente, con pinza de sujeción al pistón del sillón. Deja las herramientas al alcance de la mano durante el servicio, sin ocupar sitio ni depender de un carro auxiliar.',
    N'<p>La <strong>bandeja auxiliar ajustable Ari de Mirplay</strong> se sujeta <strong>al propio pistón del sillón</strong> y deja las herramientas justo donde hacen falta. Es un accesorio pequeño que cambia la manera de trabajar un puesto.</p>
<h2>Características de la bandeja ajustable Ari</h2>
<ul>
<li>Aluminio resistente, para uso profesional diario.</li>
<li>Pinza de sujeción al pistón del sillón, de fijación firme.</li>
<li>Solución ergonómica y práctica durante el servicio.</li>
</ul>
<h3>Por qué se engancha al sillón y no va sobre ruedas</h3>
<p>Un carro auxiliar hay que arrastrarlo y ocupa suelo. La bandeja acompaña al sillón, gira con él y no se queda atrás cuando el profesional rodea al cliente. En puestos estrechos, es la diferencia entre tener sitio para trabajar o no tenerlo.</p>
<h3>Para qué puesto está pensada</h3>
<p>Para sillones de corte y de barbería en <em>barberías y peluquerías</em> donde cada puesto tiene que ser autónomo. El aluminio aguanta el peso de tijeras, máquinas y frascos sin ceder.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45711  COMPLEMENTO IVAR  (141.0000 EUR)  ->  https://mirplaysalon.com/producto/calentador-toallas-ivar
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45711',
    N'Calentador y esterilizador de toallas Ivar 18 L con luz UV - Mirplay',
    N'Calentador y esterilizador de toallas Ivar de Mirplay, el de mayor capacidad de la gama con 18 litros. Incorpora luz UV para esterilizar mientras mantiene la temperatura, bandeja para el agua y estante metálico extraíble. Para barberías y peluquerías de alta rotación.',
    N'<p>El <strong>calentador y esterilizador de toallas Ivar de Mirplay</strong> es el modelo de <strong>mayor capacidad de la gama, con 18 litros</strong>. Mantiene las toallas calientes y listas durante toda la jornada, que es lo que necesita una barbería con volumen para no interrumpir el ritual de la toalla caliente.</p>
<h2>Características del calentador de toallas Ivar</h2>
<ul>
<li>Capacidad de 18 litros, la más amplia de la gama.</li>
<li>Luz UV para una esterilización efectiva mientras mantiene la temperatura.</li>
<li>Bandeja para el agua incluida, para un mantenimiento más sencillo.</li>
<li>Estante metálico extraíble, para organizar mejor y limpiar con facilidad.</li>
</ul>
<h3>Calentar y esterilizar en el mismo aparato</h3>
<p>La <strong>luz UV</strong> es lo que separa a un <em>esterilizador de toallas</em> de un simple calentador. Trabaja mientras las toallas esperan a ser usadas, sin añadir un paso más al proceso ni requerir atención del profesional.</p>
<h3>Para qué negocio está pensado</h3>
<p>Para barberías y peluquerías de alta rotación, donde 18 litros de capacidad evitan quedarse sin toallas en las horas punta. La bandeja de agua y el estante extraíble simplifican la limpieza diaria del aparato.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45710  COMPLEMENTO HANK  (135.0000 EUR)  ->  https://mirplaysalon.com/producto/calentador-toallas-hank
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45710',
    N'Calentador y esterilizador de toallas Hank 10,6 L - Mirplay',
    N'Calentador y esterilizador de toallas Hank de Mirplay, en color blanco y con 10,6 litros de capacidad. Luz UV para desinfectar mientras mantiene la temperatura, bandeja para el agua y estante metálico extraíble. La medida intermedia de la gama.',
    N'<p>El <strong>calentador y esterilizador de toallas Hank de Mirplay</strong> mantiene las toallas calientes y desinfectadas durante toda la jornada. Con sus <strong>10,6 litros</strong> es la medida intermedia de la gama: suficiente para el ritmo de una barbería normal, sin ocupar el espacio del modelo grande.</p>
<h2>Características del calentador de toallas Hank</h2>
<ul>
<li>Capacidad de 10,6 litros.</li>
<li>Luz UV para una desinfección eficaz mientras mantiene la temperatura.</li>
<li>Bandeja para el agua incluida, para un mantenimiento más sencillo.</li>
<li>Estante metálico extraíble, para organizar y limpiar con facilidad.</li>
<li>Acabado en color blanco.</li>
</ul>
<h3>Qué medida elegir</h3>
<p>La capacidad se elige por el ritmo del local, no por el tamaño del salón: si el <em>ritual de toalla caliente</em> entra en casi todos los servicios, conviene subir de medida para no quedarse corto en las horas punta. Hank cubre el uso normal; para alta rotación está el modelo de 18 litros de la misma gama.</p>
<h3>Higiene sin un paso más</h3>
<p>La luz UV trabaja mientras las toallas esperan, así que la desinfección no añade ninguna tarea a la jornada. El acabado blanco encaja en cabinas de estética tanto como en barberías.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45717  COMPLEMENTO PAUL  (108.0000 EUR)  ->  https://mirplaysalon.com/producto/poste-de-barbero-paul
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45717',
    N'Poste de barbero Paul de acero inoxidable - Mirplay',
    N'Poste de barbero Paul de Mirplay, con estructura de acero inoxidable, acrílico transparente y cabezales de ABS galvanizado en negro. Luz interior y sistema giratorio, apto para interior o exterior protegido, con kit de instalación incluido.',
    N'<p>El <strong>poste de barbero Paul de Mirplay</strong> es el reclamo clásico del oficio resuelto con materiales serios: <strong>estructura de acero inoxidable</strong> y acrílico transparente, para un elemento que va a estar encendido y girando todos los días del año.</p>
<h2>Características del poste de barbero Paul</h2>
<ul>
<li>Estructura de acero inoxidable y acrílico transparente.</li>
<li>Luz interior y sistema giratorio.</li>
<li>Cabezales de ABS galvanizado en color negro.</li>
<li>Apto para interior o exterior protegido.</li>
<li>Kit de instalación incluido.</li>
</ul>
<h3>Por qué el acero inoxidable importa aquí</h3>
<p>Un <em>poste de barbero</em> suele colgar junto a la puerta, donde le llega humedad y cambios de temperatura. El inoxidable es lo que evita que a los dos años la sujeción sea el punto feo de la fachada.</p>
<h3>Un elemento de marca</h3>
<p>Se reconoce sin leer nada y funciona de día y de noche. Es de los pocos elementos de <em>mobiliario de barbería</em> que trabajan también cuando el local está cerrado.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 45713  COMPLEMENTO LAYLA  (82.0000 EUR)  ->  https://mirplaysalon.com/producto/alfombra-antifatiga-layla
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '45713',
    N'Alfombra antifatiga Layla para puesto de trabajo - Mirplay',
    N'Alfombra antifatiga Layla de Mirplay, rectangular y con espacio para alojar la base del sillón, de modo que queda fija y no se desplaza. Superficie de PVC resistente y fácil de limpiar. Pensada para prevenir dolores y favorecer una postura correcta en jornadas largas.',
    N'<p>La <strong>alfombra antifatiga Layla de Mirplay</strong> ataca un problema que casi nadie contabiliza al montar un salón: las horas de pie sobre un suelo duro. Su superficie amortigua la pisada y ayuda a <strong>prevenir el dolor y a mantener una postura correcta</strong> durante jornadas largas.</p>
<h2>Características de la alfombra antifatiga Layla</h2>
<ul>
<li>Superficie de PVC, resistente y duradera.</li>
<li>Diseño antifatiga, ayuda a mejorar la postura.</li>
<li>Forma rectangular con espacio para alojar la base del sillón.</li>
<li>Fácil de limpiar, apta para barbería y peluquería.</li>
</ul>
<h3>El hueco para la base del sillón</h3>
<p>El recorte que aloja la base del sillón no es un detalle estético: es lo que impide que la alfombra se desplace cada vez que el profesional gira alrededor del puesto. Sin ese anclaje, una <em>alfombra antifatiga</em> acaba arrugada contra el rodapié en cuestión de días.</p>
<h3>Para qué profesional está pensada</h3>
<p>Para peluqueros y barberos que trabajan de pie la mayor parte de la jornada. Es una de las inversiones más baratas en <em>ergonomía para peluquería</em> y de las que antes se notan en la espalda y las piernas.</p>
<p>Colección Essentials de <strong>Mirplay</strong>, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

-- 44367  SILLON DE PEDICURA CON BAÑERA DE PIES  (526.0000 EUR)  ->  https://www.mirplaybeauty.com/products/p/anita
INSERT INTO PrestashopProductos
    (Empresa, Número, Nombre, DescripciónBreve, Descripción,
     PVP_IVA_Incluido, VistoBueno, Usuario, [Fecha Modificación])
VALUES ('1', '44367',
    N'Sillón de pedicura Anita con bañera de pies - Mirplay Beauty',
    N'Sillón de pedicura y spa Anita de Mirplay Beauty, con reposabrazos y reposapiés giratorio regulable en altura. Altura y giro del sillón ajustables mediante bomba hidráulica. Una opción práctica y económica para el servicio de pedicura en centros de estética.',
    N'<p>El <strong>sillón de pedicura Anita de Mirplay Beauty</strong> es una opción práctica y económica para incorporar o renovar el servicio de pedicura y spa de pies en un centro de estética, con los ajustes que hacen cómodo el trabajo tanto para la profesional como para la clienta.</p>
<h2>Características del sillón de pedicura Anita</h2>
<ul>
<li>Reposabrazos para mayor comodidad de la clienta.</li>
<li>Reposapiés giratorio y regulable en altura.</li>
<li>Altura y giro del sillón ajustables mediante bomba hidráulica.</li>
</ul>
<h3>El reposapiés giratorio, la clave del servicio</h3>
<p>En una pedicura se trabaja pie por pie y hay que cambiar el ángulo constantemente. Que el <strong>reposapiés gire y se regule en altura</strong> evita tener que desplazarse alrededor del sillón en cada paso del tratamiento, que es de donde salen las malas posturas.</p>
<h3>Para qué centro está pensado</h3>
<p>Para centros de estética y salones que quieren ofrecer <em>pedicura profesional</em> con un puesto específico, sin la inversión de un spa de pedicura completo. La bomba hidráulica permite adaptar la altura al puesto de trabajo de cada cabina.</p>
<p>De <strong>Mirplay Beauty</strong>, la línea de estética y wellness del fabricante, disponible en Nueva Visión.</p>',
    NULL, 1, 'SEO Mirplay 09/09/26', GETDATE());

    -- Encolar las 25 para que el job de Hangfire las publique hacia la tienda.
    INSERT INTO Nesto_sync (Tabla, ModificadoId, Usuario, FechaModificacion)
    SELECT 'Productos', RTRIM(p.Número), 'SEO Mirplay', GETDATE()
    FROM Productos p
    WHERE p.Empresa = '1' AND RTRIM(p.Número) IN (
        '45735',
        '45732',
        '45729',
        '45723',
        '45821',
        '45812',
        '45820',
        '45811',
        '45817',
        '45822',
        '45818',
        '44497',
        '45800',
        '45750',
        '45788',
        '45799',
        '45854',
        '45849',
        '45851',
        '45749',
        '45711',
        '45710',
        '45717',
        '45713',
        '44367')
      AND NOT EXISTS (SELECT 1 FROM Nesto_sync ns
                      WHERE ns.Tabla = 'Productos'
                        AND ns.ModificadoId = RTRIM(p.Número)
                        AND ns.Sincronizado IS NULL);

    SELECT @@ROWCOUNT AS Encoladas;   -- debe dar 25

-- COMMIT TRAN;    <-- si Encoladas = 25 y los 25 INSERT han ido bien
-- ROLLBACK TRAN;  <-- en cualquier otro caso

*/

-- =============================================================================
-- 3) SEGUIMIENTO (mientras el job va drenando, cada 5 minutos)
-- =============================================================================
/*
    SELECT SUM(CASE WHEN Sincronizado IS NULL THEN 1 ELSE 0 END) AS Pendientes,
           SUM(CASE WHEN Sincronizado IS NOT NULL THEN 1 ELSE 0 END) AS Publicadas
    FROM Nesto_sync WHERE Tabla = 'Productos' AND Usuario = 'SEO Mirplay';
*/

-- =============================================================================
-- MARCHA ATRAS
-- =============================================================================
/*
    -- Deja de mandar los textos (la tienda conserva lo ultimo recibido):
    UPDATE PrestashopProductos SET VistoBueno = 0
    WHERE Empresa = '1' AND Usuario = 'SEO Mirplay 09/09/26';

    -- O borra las filas por completo:
    DELETE FROM PrestashopProductos
    WHERE Empresa = '1' AND Usuario = 'SEO Mirplay 09/09/26';
*/
