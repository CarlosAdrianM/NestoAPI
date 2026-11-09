/*
    Novedades de la versión 1.10.58.0 de Nesto (09/11/2026).
    Nesto #631 (botonera nueva del pedido), #633 (Intro en la cantidad → buscador), #627 (direcciones extranjeras), «Ver anuladas» en
    Eventos, extracto de Cobros con 5 filas, «Anular enlace», parte Nesto de NestoAPI#590 (Quitar picking), #784 (inventario), #841
    (días sin reparto propio), #843 (importe mínimo de compra en eventos), #623 corte 4 (leads de Meta) y la sugerencia 1130;
    NestoAPI #593 c4 (lo «a recoger» cuenta para el mínimo del cheque), #831 (Insertar e Imprimir de un aplazado por nuestra ruta),
    #606 (fecha prometida por nuestra ruta sin día de reparto), #634 (aviso a la tienda por una diferencia de reposición), #844 (cierre
    de caja obligatorio), #846 (correo de fecha en riesgo), enlaces de pago, conceptos de cobro con el repartidor y «Cerrada» en Novedades.

    ORDEN:
      1) Scripts sa ANTES de publicar la API:
         a) Issue590_fnEsNestoViejo.sql (pieza común; no cambia datos ni permisos) y DESPUÉS Issue590_Inventario_BloqueoNestoViejo.sql
            (desde hoy Nesto viejo no cuenta, ni contabiliza, ni deshace inventario: lo dice la novedad de abajo).
         b) Issue848_OrigenesCanal.sql SOLO si #848 entra en master antes de publicar (la API nueva lee OrigenesCliente.EsLead: sin la
            columna, el catálogo de orígenes sale vacío). Si #848 no entra, no lanzarlo.
         c) Cuando Carlos quiera (sombra, no bloquean nada): Issue590_SombraEscriturasNestoViejo_XE.sql,
            Issue590_SombraLecturasFacturasNestoViejo_XE.sql e Issue590_DireccionesClientes_BloqueoNestoViejo.sql (empieza en Sombra).
         Ya aplicados en producción (comprobado el 09/11 por lectura): Issue841_FestivosRepartoPropio.sql (CHECK con RepartoPropio),
         Issue843_EventosImporteMinimoRequeridos.sql, Issue623_ClientesOrigenLeadExterno.sql y Sugerencia1141_EventosVerAnuladas.sql.
         Pendientes de Carlos (con su OK, no son del deploy): OneShot_20261109_Ubicaciones_Abono930127.sql, las propuestas
         prdQuedaDescuadradaUbicacion_Reactivar.sql e Issue590_Productos_PVPyPrecioMedioSoloCompras.sql, y los dos de Identity
         (OneShot_20261109_Identity_*.sql, en DC2016: comprobar si ya se lanzaron).
      2) Publicar la API (master) → ClickOnce (Clean + Rebuild) → 1.10.58.0 → Ariadna 2.32.
      3) DESPUÉS de publicar la API y la ClickOnce:
         a) Comprobar en producción Picking › «Quitar picking del pedido» y, después, Issue590_QuitarPicking_BloqueoNestoViejo.sql
            (la guarda que impide quitar el picking desde Nesto viejo).
         b) ESTE script, Novedades_Ariadna_2_32.sql y OneShot_20261109_CerrarSugerencias_1_10_58_0.ps1 (marca Implementadas las
            sugerencias y contesta en cada hilo; lo lanzo yo).
         c) VÍDEO (el único de esta versión): subo botonera-nueva.mp4 (scratchpad hf-botonera de la sesión del 09/11) a la sugerencia
            1144, que es la novedad de la botonera nueva, con POST api/Novedades/1144/Video, después del OneShot.
         d) AvisarNuevaVersionNesto.ps1 -Version 1.10.58.0 solo con el OK de Carlos.
      4) DESPUÉS de que Ariadna 2.32 llegue a la prueba cerrada: Issue844_GlobosCierreCaja.sql (globos de «Cerrar caja»; VersionDesde 2.32).
      NO duplicar: van por el OneShot (la sugerencia ES la novedad) y aquí no llevan fila: 1060 (Paloma, Intro en la cantidad), 1058
      (Enrique, direcciones extranjeras), 1141 (Paloma, «Ver anuladas»), 823 (Aida, extracto de Cobros), 1130 (Laura, cheque de los
      pedidos web), 1129 (Alfredo, lo «a recoger» cuenta para el cheque), 1116 (Pilar, cursos en el cheque), 1139 (Magan, «Anular
      enlace»), 1140 (Alfredo, enlaces de otros pedidos), 1131 (Alfredo, terminar el picking), 853 (Lidia, correo de fecha en riesgo),
      1144 (botonera nueva, con el vídeo) y 1132 (Paloma, importe mínimo de compra en eventos). Las de Ariadna (1135, 1133) van con la 2.32.
      PENDIENTE si entra #847/#848: 1103 (Alfredo, reimprimir la etiqueta del reparto propio) y 1128 (Laura, orígenes de canal) están
      comentadas en el OneShot; si entran, descomentarlas allí y añadir «los orígenes del cliente (Laura)» al agradecimiento de abajo.
      Las sugerencias del hilo del inventario (1057, 1136-1138, 1142…) se tratan aparte.
      Se omiten a propósito (nada perceptible en Nesto o va por otro lado): las sombras y bloqueos de Nesto viejo salvo el del inventario,
      #547 (sombra de precios medios), el texto del correo de envío devuelto para quien no usa Nesto (va con NestoApp#273), el cierre
      automático de Novedades que avisa también al autor (va dentro de «Cerrada») y la parte Ariadna de #590/#784/#844.

    Ejecutar en SSMS contra NV como sa. Idempotente (compara por versión y título). Los literales van con N'...'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.58.0';
DECLARE @fecha DATE = '2026-11-09';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Gracias, Paloma, Alfredo, Pedro, Laura, Aida, Enrique y Lidia',
     N'Esta versión sale en buena parte de lo que nos han contado por «Novedades»: el cursor que vuelve al buscador, las inscripciones anuladas fuera de la lista y el curso por compra con importe mínimo (Paloma), el picking que no se podía terminar y la etiqueta que no salía por un enlace de pago antiguo (Alfredo y Pedro), el cheque regalo de los pedidos de la web (Laura), ver más filas en Cobros (Aida), las direcciones de Francia (Enrique) y el correo de pedidos en riesgo (Lidia). Muchas gracias: sigan contándonos.',
     NULL),
    ('Nuevo', N'Picking: «Quitar picking del pedido» en la ventana de Picking',
     N'En la ventana de Picking (la de «Sacar picking») hay un botón nuevo, «Quitar picking del pedido», para Almacén y Dirección: suelta lo reservado y quita el picking de todas las líneas pendientes del pedido. Si el mozo ya ha leído productos de ese pedido en la PDA, antes de tocar nada Nesto enseña la lista («20 Agujas Miliun (12485), leído por Pedro…») y pide confirmar; al confirmar, a la PDA le llega al momento el aviso de apartar esos productos y el pedido desaparece de su recogida. No se puede si alguna línea ya está en albarán, si el pedido ya se está empaquetando o si el picking ya está terminado. Desde ahora el picking se quita aquí, no en Nesto viejo.',
     N'Almacén,Dirección'),
    ('Mejorado', N'Inventario: sin filas repetidas y «¿Contar otra vez?»',
     N'Al contar un producto que ya tiene fila ese día, se suma a esa fila en vez de crear otra. Si lo que va a contar ya se contabilizó hoy, Nesto lo dice y pregunta «¿Contar otra vez?» (No por defecto), para no volver a abrir sin querer lo que ya estaba hecho. Al contabilizar, un producto que se volvió a abrir y sigue con 0 contado no se contabiliza salvo que marque «He contado y de verdad hay 0». Si un producto tiene filas repetidas, no se puede contabilizar hasta arreglarlo: Nesto dice cuáles son.',
     N'Almacén,Tiendas,Dirección'),
    ('Mejorado', N'El inventario ya solo se hace en Nesto y en Ariadna',
     N'Desde hoy, en Nesto viejo ya no se puede contar, contabilizar ni deshacer inventario: si se intenta, sale el aviso «Esto ya solo se puede hacer desde Nesto» y no se guarda nada. El inventario se cuenta y se contabiliza en Nesto (pestaña Productos, grupo «Inventarios») o en la PDA con Ariadna. Si hay que deshacer uno, avise a Informática.',
     N'Almacén,Tiendas,Administración,Dirección'),
    ('Nuevo', N'Días sin reparto propio (sin furgoneta)',
     N'En Festivos hay un tipo nuevo, «Sin reparto propio (furgoneta)», para los días en que no sale la furgoneta aunque los almacenes y las tiendas abran. Lo dan de alta y de baja Almacén (solo ese tipo), Administración y Dirección. Ese día no se reparte por nuestra ruta (AT y 16): las fechas de entrega, el picking de nuestra ruta y las reposiciones de las tiendas pasan al siguiente día de reparto. En Picking y en Agencias (con «Reparto propio» elegido) sale el aviso «Hoy no hay reparto propio: las entregas de nuestra ruta se mueven al siguiente día de reparto».',
     N'Almacén,Administración,Dirección,Vendedores'),
    ('Corregido', N'Fecha de entrega por nuestra ruta: el mismo día del picking también sin día de reparto',
     N'Desde la versión anterior lo de nuestra ruta se entrega el mismo día en que sale el picking, pero la fecha de entrega que proponía el pedido para los clientes de la ruta 16 sin día de reparto seguía contando como por agencia, con la entrega al día siguiente (un cliente que cierra los lunes no salía el viernes). Ahora la fecha propuesta es la del picking: ese cliente sale el viernes y, si cierra justo el día del picking, el siguiente día que abre. El motivo de la fecha dice «por nuestra ruta».',
     N'Vendedores,Administración,Almacén'),
    ('Corregido', N'Agencias: «Insertar e Imprimir» saca la etiqueta de nuestra ruta de un pedido aplazado',
     N'Con un pedido de nuestra ruta con fecha de entrega futura (por ejemplo, para mañana), «Insertar e Imprimir» en Agencias daba el error «está aplazado» y no salía la pegatina. Ahora sale la etiqueta RP con la fecha de reparto, como al cerrar el bulto. Con las demás agencias, un pedido aplazado sigue sin poder grabarse, y uno retenido no sale con ninguna.',
     N'Almacén'),
    ('Mejorado', N'Eventos: pasar de pago a gratuito con inscritos explica qué hacer',
     N'Si un evento ya tiene inscritos no se puede pasar de pago a gratuito ni al revés. Ahora, al guardar, Nesto explica por qué y qué hacer: anular antes las inscripciones, abonar la factura si ya está facturada, crear otro evento o dejar importe en la otra señal. Y si al guardar un evento hay cualquier rechazo, lo dice claramente: no se ha guardado ningún cambio y «Recargar» vuelve a enseñar lo guardado.',
     N'Administración,Tiendas,Dirección'),
    ('Nuevo', N'Clientes: «Importar leads de Meta»',
     N'En la cinta de Clientes, junto a «Crear Cliente», el botón «Importar leads de Meta»: se elige el origen (la campaña) y el fichero que exporta Meta (Excel o CSV), y la lista enseña lead a lead qué va a pasar (se creará, ya existía, o error y su motivo) sin crear nada todavía. «Importar» lo hace de verdad tras confirmar y enseña los números de cliente creados. Volver a importar el mismo fichero no crea nada dos veces.',
     N'Dirección'),
    ('Mejorado', N'Reparto propio: los cobros llevan el nombre del repartidor y la caja se cierra siempre',
     N'Los conceptos de los cobros de nuestra ruta llevan el nombre del repartidor en vez de «Reparto propio»: «S/Pago pedido 929970 a Miguel c/26223» y, en los cobros de deudas, «Pago c/12197/2 Factura NV2617853/1 a Miguel». Además, el repartidor ya no puede terminar la jornada con dinero en la caja ni empezar el día siguiente con dinero de días anteriores: tiene que cerrar la caja en la PDA, y el dinero pasa a la caja pendiente de recibir para que Administración lo recoja. Si lo que cuenta no cuadra con lo que tiene la caja, a Administración le llega un aviso urgente para revisarlo.',
     N'Administración,Dirección'),
    ('Mejorado', N'Novedades: las sugerencias contestadas pasan a «Cerrada»',
     N'Las sugerencias tienen un estado nuevo, «Cerrada»: ya está contestada y no queda nada por hacer. Quien la escribió la sigue viendo 14 días, con el aviso «Cerrada: si necesita algo más, escriba y se reabre»; en cuanto alguien escribe en ella, vuelve a estar abierta. Las que se quedan sin respuesta después de los recordatorios también se cierran así, y el aviso del cierre le llega también a quien la escribió.',
     NULL);

INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;
SELECT Id, Categoria, Titulo FROM dbo.Novedades WHERE Version = @version ORDER BY Id;
