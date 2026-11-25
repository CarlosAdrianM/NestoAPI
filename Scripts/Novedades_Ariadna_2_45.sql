/*
    Novedades de Ariadna 2.45 (25/11/2026): en el recorrido, leer la etiqueta del hueco de una muestra cuando el almacén lo exige, y
    «Sin etiqueta» (gemela de NestoAPI#884). Ya subida a los canales interno y de prueba cerrada.
    NECESITA la API del 25/11/26 publicada ANTES (lectura obligatoria por almacén y POST …/MuestraSinEtiqueta). El interruptor
    Muestras.PickingLecturaObligatoria.{ALG|REI|ALC} nace APAGADO en los tres almacenes: hasta que Almacén lo encienda, todo sigue como hoy.
    La segunda novedad (la etiqueta de un inmovilizado) es de la API del 25/11/26 y se ve en Ariadna sin cambiar nada de la app.
    Ejecutar contra NV como sa cuando la 2.45 llegue a las PDA. Idempotente (compara por Ámbito + Título).
*/
SET NOCOUNT ON;
USE NV;

DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Mejorado', N'Recorrido: leer la etiqueta del hueco de una muestra, cuando su almacén lo pida',
 N'Almacén puede pedir, almacén por almacén, que en el recorrido de recogida las muestras se cuenten LEYENDO su etiqueta del hueco (la del cajón o la gaveta). Hoy no está encendido en ningún almacén: todo sigue como siempre hasta que se avise. Cuando se encienda, teclear la referencia de una muestra con etiqueta solo la localiza y no la cuenta: lea la etiqueta y diga cuántas. Si no tiene etiqueta o no se lee, pulse «Sin etiqueta» (F1 con el cuadro vacío), elija el motivo y podrá contarla tecleando; Almacén recibe el aviso para ponérsela. Packing, enviar y recibir reposiciones, ubicar e inventario no cambian.',
 N'Almacén,Tiendas'),
(2, N'Mejorado', N'La etiqueta de un inmovilizado no se cuenta como stock',
 N'Las máquinas inmovilizadas (de demostración o de uso propio) llevan una etiqueta «INMOVILIZADO · NO ES STOCK» con un código como «I/1823». Si la lee en el inventario o al buscar un producto, Ariadna le dice que es un inmovilizado y que no se cuenta ni se prepara.',
 N'Almacén,Tiendas');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.45', '20261125', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
