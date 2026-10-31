/*
    30/10/26: al cambiar la contraseña de nesto@nuevavision.es (había caducado), el correo de la API se arregló
    copiando secretos.config y reciclando el pool, pero Database Mail de SQL Server guarda SU PROPIA copia de la
    contraseña en la cuenta del perfil «Nesto». Desde entonces fallan los correos que mandan los procedimientos
    almacenados y los triggers con sp_send_dbmail @profile_name = 'Nesto', por ejemplo:
      - «Envío Venta Online» a tiendaonline@ (prdCrearAlbaránVta, al albaranear WEB/STK/QRU/BLT): sugerencia 905 de Enrique.
      - «Aviso de Devolución de Producto» (prdCrearAlbaránVta) y los «Correo (Id.: …) en cola» de los scripts.

    Lanzar como sa en DC2016. Paso 1 solo mira. Paso 2: escribir la contraseña nueva donde pone <CONTRASEÑA NUEVA>
    (NO guardar el fichero con ella) y ejecutar solo ese bloque.
*/
USE msdb;
SET NOCOUNT ON;

-- 1) Qué cuenta usa el perfil «Nesto» y cuántos correos han fallado hoy
SELECT p.name AS Perfil, a.name AS Cuenta, a.email_address, s.servername, s.port, s.enable_ssl, s.username
FROM dbo.sysmail_profile p
JOIN dbo.sysmail_profileaccount pa ON pa.profile_id = p.profile_id
JOIN dbo.sysmail_account a ON a.account_id = pa.account_id
JOIN dbo.sysmail_server s ON s.account_id = a.account_id
WHERE p.name = 'Nesto';

SELECT sent_status, COUNT(*) AS Correos, MIN(send_request_date) AS Desde, MAX(send_request_date) AS Hasta
FROM dbo.sysmail_allitems
WHERE send_request_date >= DATEADD(day, -2, GETDATE())
GROUP BY sent_status;

SELECT TOP 20 l.log_date, LEFT(l.description, 200) AS Error
FROM dbo.sysmail_event_log l
WHERE l.event_type = 'error' AND l.log_date >= DATEADD(day, -2, GETDATE())
ORDER BY l.log_date DESC;

/* 2) Poner la contraseña nueva. La cuenta es «Nesto» (account_id 1). sysmail_update_account_sp exige
      @username junto a @password; se pasan también el resto de datos tal como están, para no dejar ninguno a NULL.
EXEC dbo.sysmail_update_account_sp
     @account_name = N'Nesto',
     @email_address = N'nesto@nuevavision.es',
     @mailserver_name = N'smtp.office365.com',
     @port = 587,
     @enable_ssl = 1,
     @username = N'nesto@nuevavision.es',
     @password = N'<CONTRASEÑA NUEVA>';

-- Prueba: debe llegar a carlosadrian@
EXEC dbo.sp_send_dbmail @profile_name = 'Nesto', @recipients = 'carlosadrian@nuevavision.es',
     @subject = 'Prueba Database Mail tras cambiar la contraseña', @body = 'Si le llega, Database Mail vuelve a funcionar.';
*/

/* 3) SOLO cuando la prueba del paso 2 haya llegado: reenviar los que fallaron desde el 28/10 20:25 (la contraseña
      caducó entonces; 39 correos a 30/10 11:21). Cada uno sale igual que el original, con «[Reenviado]» en el asunto.
      Primero se ve la lista; después se ejecuta el bucle.
SELECT mailitem_id, send_request_date, recipients, copy_recipients, subject
FROM dbo.sysmail_faileditems
WHERE send_request_date >= '20261028 20:00'
ORDER BY send_request_date;

DECLARE @id int, @to varchar(max), @cc varchar(max), @bcc varchar(max), @subject nvarchar(255), @body nvarchar(max),
        @formato varchar(20), @n int = 0;
DECLARE c CURSOR LOCAL FAST_FORWARD FOR
    SELECT mailitem_id, recipients, copy_recipients, blind_copy_recipients, subject, body, body_format
    FROM dbo.sysmail_faileditems
    WHERE send_request_date >= '20261028 20:00'
    ORDER BY send_request_date;
OPEN c;
FETCH NEXT FROM c INTO @id, @to, @cc, @bcc, @subject, @body, @formato;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @subject = LEFT(N'[Reenviado] ' + ISNULL(@subject, N''), 255);
    EXEC dbo.sp_send_dbmail @profile_name = 'Nesto', @recipients = @to, @copy_recipients = @cc,
         @blind_copy_recipients = @bcc, @subject = @subject, @body = @body, @body_format = @formato;
    SET @n += 1;
    FETCH NEXT FROM c INTO @id, @to, @cc, @bcc, @subject, @body, @formato;
END
CLOSE c; DEALLOCATE c;
PRINT CONCAT('Reenviados: ', @n);
*/
