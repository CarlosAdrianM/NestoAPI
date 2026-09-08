/*
    OneShot 08/09/26 (d) - Meter en Nesto las 5 descripciones que escribió el equipo de SEO.

    POR QUÉ VAN EN LA COLUMNA Descripcion Y NO EN UNA NUEVA
    -------------------------------------------------------
    Se habló de crear una columna aparte para las descripciones escritas a mano, porque hoy no hay
    forma de editarlas (el listado de vídeos es de solo lectura y no existe endpoint para ello).
    Para ESTOS CINCO no hace falta, y conviene no esperar:

      - Los cinco tienen la Descripcion VACÍA (comprobado el 08/09/26). No se pisa nada.
      - DescripcionFichaVideo.Componer ya devuelve Descripcion como primera opción, así que el texto
        escrito a mano gana automáticamente al extracto del protocolo y ganará también a la
        descripción generada cuando esa exista. Es exactamente la precedencia que pidió SEO.
      - NVIA solo escribe Descripcion al INSERTAR un vídeo nuevo; nunca actualiza los que ya están.
        Así que esto no lo pisa nadie.

    Resultado: los cinco entran con la publicación de esta semana, sin esperar al proyecto de
    generación de descripciones.

    QUÉ CAMBIA EN LA TIENDA
    -----------------------
    Los cinco pasan a tener descripción propia en su ficha. Dos de ellos (1667 y 1830) están además
    en la lista de los que iban a llevar extracto del protocolo o nada: ahora llevan el texto bueno.

    ES REVERSIBLE
    -------------
    Para deshacerlo:  UPDATE Videos SET Descripcion = '' WHERE Id IN (1830,1819,1661,1407,1667);
    (Estaban vacías, así que volver atrás es dejarlas vacías otra vez.)
*/

USE NV;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Red de seguridad: si alguna dejó de estar vacía, es que alguien la escribió por otro lado.
IF (SELECT COUNT(*) FROM Videos
    WHERE Id IN (1830, 1819, 1661, 1407, 1667)
      AND LTRIM(RTRIM(ISNULL(Descripcion, ''))) = '') <> 5
BEGIN
    RAISERROR('Alguno de los 5 ya tiene descripción: no se pisa. Revisar a mano. Se aborta.', 16, 1);
    RETURN;
END

BEGIN TRANSACTION;

UPDATE Videos SET Descripcion = N'Las dos foliculitis y en qué se diferencian: la pilaris, una alteración de la queratinización, y la infecciosa, que aparece tras la depilación. La pilaris sale en los laterales externos de brazos, piernas y glúteos, como un montículo duro y áspero sobre cada folículo, y no depende del pelo. La infecciosa la provoca una bacteria que coloniza el folículo cuando el manto hidrolipídico y la microbiota están dañados: de ahí la insistencia en la higiene, la hidratación y en no abusar de los productos alcohólicos, que fragilizan la microbiota. La clase termina con las lesiones eccematosas y con un recordatorio que se usa a diario en cabina: la epidermis es avascular, así que la capa córnea no se hidrata bebiendo agua. Capítulo 8 de «Estética desde 0».'
WHERE Id = 1830;

UPDATE Videos SET Descripcion = N'Después de una higiene facial o de un tratamiento antiarrugas no conviene hacer deporte, ni sauna, ni exponerse al calor. La razón es que son tratamientos que ya estimulan mucho la circulación, así que todo lo que la estimule más juega en contra del resultado. Respuesta a la duda de una profesional que avisa siempre de ello en cabina.'
WHERE Id = 1819;

UPDATE Videos SET Descripcion = N'¿Se puede aplicar vitamina C con dermapen? No es la vitamina más adecuada para la técnica, y aquí se explica por qué. Sola es altamente oxigenante, y ese exceso de oxigenación y de riego sanguíneo puede provocar una reacción circulatoria. Distinto es que forme parte de un cóctel junto a otros activos: esa es la recomendación.'
WHERE Id = 1661;

UPDATE Videos SET Descripcion = N'¿Qué trae la línea R+Structurant de UFAES para el cabello castigado? Champú, mascarilla y bálsamo, y aquí se ve qué hace cada uno. El champú está formulado para la higiene del cabello castigado y elimina la electricidad estática. La mascarilla suaviza, hidrata y controla el encrespamiento. Y el bálsamo, nutritivo, contribuye a cerrar las puntas abiertas. Presentación de la línea como novedad de la feria online.'
WHERE Id = 1407;

UPDATE Videos SET Descripcion = N'Qué es el fototipo y por qué condiciona el resto del tratamiento: es la capacidad genética de la piel para producir melanina. El diagnóstico empieza por identificarlo, con una evaluación completa de la piel, porque de ahí depende poder adaptar el tratamiento estético con precisión a cada cliente. Capítulo 4 de «Estética desde 0».'
WHERE Id = 1667;

-- Verificación antes del commit
IF (SELECT COUNT(*) FROM Videos
    WHERE Id IN (1830, 1819, 1661, 1407, 1667)
      AND LEN(ISNULL(Descripcion, '')) BETWEEN 200 AND 800) <> 5
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('Alguna descripción no ha quedado con la longitud esperada: ROLLBACK.', 16, 1);
    RETURN;
END

COMMIT TRANSACTION;

SELECT 'Id=' + CAST(Id AS varchar) + ' | ' + RIGHT('   ' + CAST(LEN(Descripcion) AS varchar), 3)
     + ' caracteres | ' + LEFT(Descripcion, 60) + '...'
FROM Videos WHERE Id IN (1830, 1819, 1661, 1407, 1667) ORDER BY Id;

SELECT 'Vídeos sin descripción que quedan = ' + CAST(COUNT(*) AS varchar)
FROM Videos WHERE LTRIM(RTRIM(ISNULL(Descripcion, ''))) = '' AND FechaBaja IS NULL;
GO
