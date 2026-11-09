/*
    NestoAPI#848 (Carlos 09/11/26, tras ver la guía de orígenes): el origen es NUESTRO PRIMER CONTACTO con el cliente, no quién le
    abre la ficha. Faltaba el caso más habitual, el vendedor que descubre el centro en sus visitas, y el cliente que viene recomendado.
    - VISITA-VENDEDOR «Visita del vendedor» y RECOMENDACION «Recomendación»: de canal (EsLead = 0, no son leads que haya que llamar).
    - Descartados por Carlos: «Llamada o correo entrante» (no dice cómo nos conoció) y «Ferias» (ya no vamos). Si no se sabe: vacío.
    Como sa en NV, cuando quiera (la API ya lee EsLead desde Issue848_OrigenesCanal.sql). Idempotente. Sin GRANTs nuevos.
*/
SET NOCOUNT ON;
USE NV;
GO
IF COL_LENGTH(N'dbo.OrigenesCliente', N'EsLead') IS NULL
    RAISERROR('Falta la columna EsLead: lanzar antes Issue848_OrigenesCanal.sql', 16, 1);
GO
IF COL_LENGTH(N'dbo.OrigenesCliente', N'EsLead') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.OrigenesCliente WHERE Codigo = 'VISITA-VENDEDOR')
        EXEC(N'INSERT INTO dbo.OrigenesCliente (Codigo, Descripcion, Campana, FechaDesde, FechaHasta, Activo, HorasParaLlamar, EsLead, Usuario)
               VALUES (''VISITA-VENDEDOR'', N''Visita del vendedor'', NULL, NULL, NULL, 1, 24, 0, N''Issue848b'')');
    IF NOT EXISTS (SELECT 1 FROM dbo.OrigenesCliente WHERE Codigo = 'RECOMENDACION')
        EXEC(N'INSERT INTO dbo.OrigenesCliente (Codigo, Descripcion, Campana, FechaDesde, FechaHasta, Activo, HorasParaLlamar, EsLead, Usuario)
               VALUES (''RECOMENDACION'', N''Recomendación'', NULL, NULL, NULL, 1, 24, 0, N''Issue848b'')');
END
GO
-- Comprobación: 8 filas; solo META-HARMONIA-0926 con EsLead = 1
SELECT Codigo, Descripcion, Activo, EsLead, Usuario FROM dbo.OrigenesCliente ORDER BY EsLead DESC, Descripcion;
