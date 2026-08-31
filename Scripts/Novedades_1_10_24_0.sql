/*
    Novedades de la versión 1.10.24.0 (31/08/2026).

    Sube la tercera cifra porque la ventana de Ofertas estrena DOS pestañas completas.

    SE OMITE TODO LO DEMÁS A PROPÓSITO (nada de esto lo nota el usuario):
      - El arreglo del reembolso que dejaba el envío sin cerrar: solo afecta al piloto de la
        tramitación por API, que hoy son dos usuarios, y se cuenta directamente a quien lo sufrió.
        Además desde fuera se ve como que "vuelve a funcionar", no como una novedad.
      - Que el motor de precios respete las fechas de vigencia: es la maquinaria de debajo de las
        dos pestañas nuevas, y lo que el usuario percibe ya se cuenta en ellas.
      - El nombre de campaña en los descuentos y las operaciones en bloque por campaña: van dentro
        de la pestaña de Campañas, que ya se cuenta.
      - El subgrupo "Presoterapia profesional" y sus 16 referencias: es un alta de datos, no una
        funcionalidad, y se ve sola en la tienda.
      - El código de la ficha de la familia en el mensaje de productos, y la unificación de la
        regla de vigencia entre descuentos y ofertas. Refactor.

    Ejecutar en SSMS contra NV DESPUÉS de publicar.
*/

SET NOCOUNT ON;
USE NV;

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
('1.10.24.0', '2026-08-31', 'Nuevo', 'Las ofertas 6+2 ya se ponen desde Nesto, y con fechas',
 'La ventana de Ofertas estrena la pestana "Ofertas de Producto": ahi se ven y se mantienen las ofertas del tipo 6+2 de cada referencia, que hasta ahora solo se podian meter desde el Nesto antiguo. La novedad de verdad son las FECHAS: se puede decir desde cuando y hasta cuando vale una oferta, y deja de aplicarse sola el dia que toca. Antes no habia forma de fecharlas, asi que para quitar una oferta habia que borrarla a mano y acordarse de hacerlo. Se puede dejar cualquiera de las dos fechas en blanco: sin fecha de fin, la oferta vale hasta que se quite. Tambien se ve la casilla Denegar, que sirve para prohibir expresamente una oferta.', 'Nesto', 1, 'sa'),

('1.10.24.0', '2026-08-31', 'Nuevo', 'Campanas de descuento con fecha de caducidad',
 'Otra pestana nueva en la misma ventana: "Campanas". Sirve para montar los descuentos de campana (las rebajas, el Black Friday...) sin tener que pedirselos a informatica. Cada descuento puede ser de un producto, de una familia entera o de una familia dentro de un grupo, y lleva sus fechas de principio y fin: empieza y termina solo. Ademas se le pone NOMBRE a la campana, y con el se puede cerrar o borrar la campana entera de una vez en vez de fila a fila. Tambien se elige a quien se le ensena el descuento en la tienda online: solo a profesionales, o tambien al publico, con la posibilidad de darle al publico un porcentaje distinto.', 'Nesto', 1, 'sa');

SELECT @@ROWCOUNT AS Insertadas;
SELECT Id, Version, Categoria, Titulo FROM Novedades WHERE Version = '1.10.24.0' ORDER BY Id;
