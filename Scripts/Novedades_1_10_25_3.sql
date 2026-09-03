/*
    Novedades de la versión 1.10.25.3 (03/09/2026).

    Solo sube la última cifra: dos correcciones en Nesto y un buscador bastante mejor.

    SE OMITE TODO LO DEMÁS A PROPÓSITO (nada de esto lo nota el usuario de Nesto):
      - El cobro directo con la tarjeta guardada cobraba la base imponible, sin IVA ni portes
        (NestoAPI#452). Es de la app de clientes y nunca llegó a cobrarse de menos porque el
        terminal rechazaba la operación; se arregla antes de que Comercia active la operativa mixta.
      - Reindexación del buscador como tarea de Hangfire a las 20:30 (NestoAPI#402) y los dos
        arreglos de jobs del SQL Agent (#450 y #451): son scripts que se ejecutan aparte.
      - El correo interno de un pedido hecho desde la app llega con [APP] en el asunto y un fallo
        del servidor de correo queda registrado (NestoAPI#444): lo ven quienes reciben esos correos,
        no es una pantalla de Nesto.
      - Pedidos de compra automáticos: se calculan de uno en uno para que dos personas a la vez no
        se pisen. Antes fallaba con un error de tabla temporal; ahora simplemente espera su turno.

    Ejecutar en SSMS contra NV DESPUÉS de publicar.
*/

SET NOCOUNT ON;
USE NV;

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
('1.10.25.3', '2026-09-03', 'Corregido', 'En Caja ya no se pueden crear dos asientos por pulsar Contabilizar dos veces',
 'Si la contabilización tardaba, el botón Contabilizar de Cobros y de Gastos seguía activo y una segunda pulsación creaba un asiento duplicado. Ahora el botón se apaga en cuanto se pulsa, aparece el aviso de "por favor, espere" y vuelve a activarse al terminar, tanto si va bien como si da error.', 'Nesto', 1, 'sa'),

('1.10.25.3', '2026-09-03', 'Corregido', 'La plantilla no admite descuentos mayores del 100 %',
 'Al teclear un descuento con un cero de más (un 500 % en vez de un 50 %), el pedido se rechazaba al guardar con un error de base de datos incomprensible. Ahora la celda no admite un descuento fuera del 0 al 100 % y se queda con el valor anterior. Si aun así llegara uno mal al servidor, el mensaje dice qué producto y qué descuento tiene.', 'Nesto', 1, 'sa'),

('1.10.25.3', '2026-09-03', 'Mejorado', 'El buscador de productos encuentra mejor y ordena por lo que más se vende',
 'Tres mejoras en la búsqueda de productos. Los resultados salen ordenados teniendo en cuenta lo que más se vende, así que lo habitual aparece arriba: buscando "rollo papel" sale primero el rollo de papel camilla. La palabra escrita tal cual pesa más que su raíz, así que "vapore" encuentra la Vapore de Eva Visnú y no sesenta recambios de vapor. Y si te equivocas al escribir, en vez de no encontrar nada se busca por parecido y por cómo suena: "richeza" y "rikeza" encuentran la Ricchezza, y "mascariya" las mascarillas.', 'Nesto', 1, 'sa'),

('1.10.25.3', '2026-09-03', 'Corregido', 'Aceptar un presupuesto cuya línea ya no existe avisa en vez de dar un error interno',
 'Si mientras se aceptaba un presupuesto otra persona borraba una de sus líneas, salía un error interno sin explicación. Ahora avisa de que alguien ha modificado o eliminado la línea mientras se actualizaba el pedido.', 'Nesto', 1, 'sa');

SELECT Version, Categoria, Titulo FROM Novedades WHERE Version = '1.10.25.3';
