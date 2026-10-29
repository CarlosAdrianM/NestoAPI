/*
    NestoAPI#731 (sugerencia 862, Laura Camacho; decisión de Carlos 29/10/26): la persona que asiste al evento.

    En la inscripción se enseñan, SOLO LECTURA y sacados de la ficha (Clientes y PersonasContactoClientes), el nombre, el
    correo y la dirección del cliente/contacto: no se guardan. Lo que SÍ se guarda es la persona que asiste: una de las
    personas de contacto del cliente, de la misma dirección (contacto) que la inscripción.

    - EventosInscripciones.PersonaContacto: PersonasContactoClientes.Número (char(3)) de la persona que asiste, dentro de
      (Empresa, Cliente, Contacto) de la propia inscripción. NULL = sin indicar (las de antes).
      Sin clave ajena: la persona se puede borrar en Nesto y la inscripción no debe impedirlo (la API la enseña como
      «sin indicar» si ya no existe).

    La columna está FUERA del EDMX: la API la lee y la escribe por SQL y comprueba que existe (COL_LENGTH). Sin el script,
    la API funciona como antes (sin persona que asiste); el PUT para cambiarla contesta que falta el script.

    Ejecutar en SSMS contra NV como sa (ALTER). Idempotente: se puede lanzar dos veces.
    EventosInscripciones ya tenía SELECT/INSERT/UPDATE/DELETE (#611): la columna nueva no necesita GRANT.
*/

SET NOCOUNT ON;
USE NV;
GO

IF COL_LENGTH('dbo.EventosInscripciones', 'PersonaContacto') IS NULL
    ALTER TABLE dbo.EventosInscripciones ADD PersonaContacto char(3) NULL;
GO

-- Por si se lanza en una BD sin el GRANT de #611 (no hace daño repetirlo)
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.EventosInscripciones TO [NUEVAVISION\RDS2016$];
GO

-- VERIFICACIÓN: la columna existe
SELECT COL_LENGTH('dbo.EventosInscripciones', 'PersonaContacto') AS LongitudPersonaContacto;
GO
