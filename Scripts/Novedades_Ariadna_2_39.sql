/*
    Novedades de Ariadna 2.39 (19/11/26). NECESITA la API del 19/11/26 publicada ANTES («Solo entrego los leídos» y el reembolso
    parcial dependen de ella).
    Ejecutar en SSMS contra NV como sa cuando la 2.39 llegue a las PDA. Idempotente (compara por Ámbito + Título).
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Nuevo', N'Reparto: si entrega con bultos sin leer, Ariadna pregunta',
 N'Si va a entregar y no ha leído todos los bultos, Ariadna le pregunta «¿Los entrega todos?». Con F8 los entrega todos y el cliente firma todos los que lleva. Si alguno se ha quedado en el almacén, pulse F4, «Solo entrego los leídos»: el cliente firma solo esos, y el que falta queda apuntado con su número para que Almacén y el vendedor lo sepan.',
 NULL),
(2, N'Nuevo', N'Reparto: el reembolso se cobra entero o con autorización',
 N'Si teclea en efectivo menos de lo que marca el reembolso, la entrega no se manda: Ariadna le ofrece pedir autorización (Intro o F7) o corregir el importe (Esc). Con la autorización concedida, entrega cobrando lo acordado. No se puede cobrar más que el reembolso. Las notas de cobro no cambian.',
 NULL),
(3, N'Mejorado', N'Inventario: «Stock calculado» y «Stock real»',
 N'En el inventario cada producto enseña el «Stock calculado» (lo que dice Nesto, que no se puede tocar) y al lado el «Stock real», que es lo que usted cuenta. Teclee la cantidad que hay y pulse F8 para ponerla.',
 NULL),
(4, N'Mejorado', N'Las preguntas salen encima de la pantalla',
 N'Las preguntas de Ariadna («¿Quitarlo sin leer?», «¿Los entrega todos?»…) ocupan ahora toda la pantalla, con sus botones a la vista y la tecla de cada uno. Ya no quedan tapadas por otros botones.',
 NULL),
(5, N'Mejorado', N'Las cifras llevan su nombre',
 N'En las listas ya no hay números sueltos: verá «Leídas 4 de 6», «Quedan 3», «Ubicar 3» o «Lleva 3».',
 NULL),
(6, N'Mejorado', N'La dirección, con el piso o el rótulo',
 N'En el packing y en la recogida imprevista la dirección sale completa, con el piso o el rótulo del local cuando el cliente lo tiene.',
 NULL);

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.39', '20261119', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
SELECT Id, Version, Categoria, Titulo FROM Novedades WHERE Ambito = 'Ariadna' AND Version = '2.39' ORDER BY Id;
