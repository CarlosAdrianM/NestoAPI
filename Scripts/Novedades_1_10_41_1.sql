/*
    Novedades de la versión 1.10.41.1 de Nesto (14/10/2026).
    Versión de la ClickOnce: 1.10.41.1 (pubxml 1.10.41.* en revisión 1; tanda de arreglos y mejoras menores, sin subir la 3.ª cifra).

    Tanda: NestoAPI (prdCrearAlbaránCmp devoluciones MTP/CUR [publicado en BD el 14/10 08:24], #593 distribuidores sin cheque,
    #594 corte 2 registro de movimientos de ubicaciones [interruptor apagado] y cuadre, #624 altas con el correo confirmado,
    #622 la reposición del día se suma a la que la tienda tiene sin enviar + aviso en la campana, #611 cupones corte 1
    [solo API, sin pantalla todavía], script del piloto RegionesPropias)
    + Nesto (Nesto#525 modo de servicio que se perdía, #622 franja en Enviar reposición, reposición puntual reentrante,
    #490 4C.2 cierre y 4C.4 shell/cinta/regiones con el piloto apagado).

    ORDEN:
      1) Scripts sa ANTES de publicar la API:
         - Issue611_EventosInscripciones.sql                 (columnas de Eventos + tabla EventosInscripciones: están en el EDMX)
         - Issue629_IndicadoresStock.sql                     (tabla IndicadoresStock; sin ella el job solo avisa en ELMAH y el GET da 503)
      2) Scripts sa cuando se quiera (antes o después, idempotentes):
         - Issue622_ReposicionesTraspasos_IdCompletada.sql   (enlace entre la cabecera del día y la preparación completada)
         - Issue630_PropuestasStock.sql                      (tabla PropuestasStock; sin ella el job manda el Excel y avisa de que no guarda)
         - Issue594_UbicacionesMovimientos.sql               (OPCIONAL: solo si se enciende Ubicaciones:RegistrarMovimientos)
         - Nesto490_RegionesPropias.sql                      (OPCIONAL: piloto de regiones solo para Carlos)
      3) Publicar NestoAPI.
      4) Publicar la ClickOnce (Clean + Rebuild) → 1.10.41.1.
      5) DESPUÉS: ESTE script (idempotente: compara por Versión + Título).
      6) Cerrar NestoAPI#624, #622, Nesto#525 y Ariadna#24 (comentario con la versión). Sugerencia 599 (Paloma): PUT Version 1.10.41.1
         y comentario en su hilo (OneShot_20261014_CerrarSugerencias_1_10_41_1.ps1).
      7) Re-adjuntar «Guia reposiciones automaticas.pdf» (ya regenerada: Ariadna 2.7 + #622) a las novedades de reposiciones
         (552, 562, 564, 570, 577, 578 y 581) con Scripts/AdjuntarANovedad.ps1.

    SE OMITE A PROPÓSITO:
      - #611 (inscripciones a eventos): solo API; la pantalla llega con Nesto#517.
      - #594 (movimientos de ubicaciones y cuadre): sin pantalla; lo usa Dirección por la API.
      - #624 (altas con el correo confirmado): afecta a Ariadna/NestoApp al dar de alta, no a los usuarios de Nesto.
      - #593 distribuidores sin cheque, #490 (modernización), reposición puntual reentrante: sin cambio visible.
      - La devolución a proveedor que no bajaba del hueco: arreglo de BD ya en producción desde el 14/10 a las 8:24.

    Ejecutar en SSMS contra NV como sa. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.41.1';
DECLARE @fecha DATE = '2026-10-14';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Corregido', N'La plantilla ya no cambia sola el modo de servir que has elegido',
     N'Si elegías «Todo junto» (o cualquier otro modo) y después tocabas cantidades, añadías un regalo o una oferta o filtrabas las direcciones, la plantilla volvía a poner el modo que calcula ella y el pedido se grababa con otro distinto, sin avisar. Ahora, mientras la dirección sea la misma, se respeta lo que has elegido: el modo de servir, el de facturación, la forma de pago, los plazos y la cuenta. Solo se recalcula si cambias de contacto de verdad, y en ese caso te avisa. También avisa si el pedido vuelve a «Todo junto» por un cambio automático o si no se puede cambiar el modo porque ya tiene picking.',
     N'Vendedores,Tiendas,Administración'),
    ('Mejorado', N'Doble clic en la referencia de una reposición abre la ficha del producto',
     N'En Enviar reposición (también en la reposición puntual) y en las líneas de Recibir reposición, doble clic en la referencia abre la ficha del producto, como en el pedido. Al volver a la pestaña de la reposición, el cursor vuelve al cuadro del lector. El doble clic en «Cantidad» o «Leído» sigue sirviendo para escribir.',
     N'Tiendas'),
    ('Mejorado', N'Enviar reposición te dice qué ha pasado con la reposición de hoy',
     N'Arriba, en «Enviar reposición», sale lo que ha hecho el sistema con la reposición automática del día: si se ha rellenado, si se ha sumado a la que tenías sin enviar del día anterior (en vez de saltársela, como pasaba antes) o si no se ha podido rellenar y por qué (por ejemplo, un inventario en curso). Cuando pasa algo de esto también te llega un aviso a la campana. La franja desaparece en cuanto envías la reposición.',
     N'Tiendas');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;

-- Ariadna 2.7 (Ariadna#24; publicada el 14/10 en Play interna y Windows)
DECLARE @ariadna TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000));

INSERT INTO @ariadna (Categoria, Titulo, Descripcion) VALUES
    ('Mejorado', N'En las salidas, leer el código comprueba el producto y la cantidad se teclea entera',
     N'Al preparar un picking o una reposición, la lectura del código ya no cuenta una unidad: solo comprueba que tienes en la mano el producto que toca (si la línea es de una unidad, la cuenta y pasa al siguiente, como antes). Comprobado, el cuadro cambia a «CANTIDAD» (en ámbar) y tecleas cuántas coges en total: para 100 unidades, 100 e Intro. Intro sin nada manda todas las que pide. Leer el código otra vez ya no suma: te avisa de que ya está comprobado. Si prefieres contar leyendo unidad a unidad, F1 con el cuadro vacío (o el botón «Contar leyendo») en esa línea. Si ya habías contado una parte y tecleas más, se suma y te lo dice con un aviso y un pitido distinto.'),
    ('Mejorado', N'Se ve en qué paso estás: «LEE EL CÓDIGO» o «CANTIDAD»',
     N'El cuadro de lectura lleva encima una etiqueta grande con lo que toca hacer: «LEE EL CÓDIGO» (o «TECLEA LA REFERENCIA» si el producto no tiene código) y, comprobado, «CANTIDAD», con el cuadro en ámbar. En el móvil, con la cámara abierta, al comprobar salen los botones «Todas (N)» y «Teclear cuántas», que abre el teclado numérico. En Entradas no cambia nada: la lectura sigue buscando el producto y sumando.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT '2.7', @fecha, a.Categoria, a.Titulo, a.Descripcion, 'Ariadna', 1, 'sa'
FROM @ariadna a
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = a.Titulo);

SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
