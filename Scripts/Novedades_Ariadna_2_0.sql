/*
    Novedades de Ariadna 2.0 (05/10/2026): la primera tanda, para el día que entra en el almacén.

    Son las primeras con Ambito = 'Ariadna' (hasta ahora no había ninguna: la usaba solo Carlos). Las ve el mozo
    con el botón «Novedades» del inicio de Ariadna (GET api/Novedades?ambito=Ariadna), en la PDA, en el móvil y en Windows.
    Cuentan lo que ya hace Ariadna, no lo de una versión concreta.

    Ejecutar en SSMS contra NV como sa, cuando se quiera (no depende de ninguna publicación).
    Idempotente: compara por Ámbito + Versión + Título. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '2.0';
DECLARE @fecha DATE = '2026-10-05';

DECLARE @novedades TABLE (Orden int, Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000));

INSERT INTO @novedades (Orden, Categoria, Titulo, Descripcion) VALUES
    (1, 'Nuevo', N'Entrar con tu nombre y tu PIN',
     N'Tocas tu nombre y tecleas tus 4 cifras. La primera vez en cada aparato se entra con la contraseña de siempre; si no te acuerdas, en «He olvidado mi contraseña» escribes tu correo y te llega un enlace para cambiarla. En una misma PDA pueden entrar varios mozos, cada uno con lo suyo.'),
    (2, 'Nuevo', N'Salidas: el picking estantería a estantería',
     N'Eliges la recogida y Ariadna te lleva parada por parada, empezando por la que toca. Cada lectura suma una unidad y junto al nombre ves la foto del producto (tócala para verla en grande). Si falta algo, lo marcas como falta y se puede deshacer.'),
    (3, 'Nuevo', N'Packing con la foto de cada bulto',
     N'Con el botón «Empaquetar» del inicio (o al terminar el picking) haces una foto de cada caja que cierras. Vale también para lo recogido en papel: tecleas el número del picking o del pedido, o lo eliges de la lista de pendientes. Las fotos quedan con el pedido y en Agencias se ven al preparar el envío.'),
    (4, 'Nuevo', N'Entradas: recibir la mercancía',
     N'Una sola pantalla para lo que llega. Lees un producto y se abre su recepción; si es de un pedido que se había dado por no servido, Ariadna te dice de qué pedido es en vez de tratarlo como sobrante. En una reposición entra lo que lees: si no coincide con lo enviado, Ariadna te enseña las diferencias y se avisa a quien la hizo.'),
    (5, 'Nuevo', N'Ubicar lo que está pendiente de ubicar',
     N'Lees el producto y la etiqueta del hueco donde lo dejas, y queda ubicado.'),
    (6, 'Nuevo', N'Con el lector, con la cámara o con el dedo',
     N'En la PDA todo se hace con el lector y el teclado, sin tocar la pantalla: Intro sin escribir nada hace la acción principal. En un móvil sin lector se lee con la cámara. Y todo funciona también tocando la pantalla.'),
    (7, 'Nuevo', N'Aunque se vaya la red, no se pierde nada',
     N'Lo que lees se guarda en el aparato y sube solo en cuanto vuelve la conexión. En «Pendiente de subir» ves lo que queda por subir de cada mozo y recogida, y lo puedes descartar si ya no vale.'),
    (8, 'Nuevo', N'«Dato mal» en la ficha del producto',
     N'Si la foto, el precio, el código de barras o cualquier otro dato de un producto está mal, lo dices desde su ficha y le llega a quien lo puede arreglar.'),
    (9, 'Nuevo', N'Tu opinión cuenta: «Sugerir una mejora» y «Algo no funciona»',
     N'Desde Sugerencias puedes proponer mejoras o avisar de un fallo, con una captura de la pantalla si quieres. Las respuestas te llegan a la campana de avisos. Es una aplicación nueva: todo lo que nos digáis nos ayuda a mejorarla.'),
    (10, 'Nuevo', N'Ariadna también en los ordenadores con Windows',
     N'Se instala desde https://api.nuevavision.es/ariadna/ y, como Nesto, se actualiza sola: al abrirla, si hay versión nueva, avisa y se instala antes de empezar.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa'
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Version = @version AND x.Titulo = n.Titulo)
ORDER BY n.Orden;

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;

-- Comprobación: deben salir 10 filas, sin repetidos.
SELECT Id, Version, Categoria, Titulo, Ambito, Publicada
FROM Novedades WHERE Ambito = 'Ariadna' ORDER BY Id;
