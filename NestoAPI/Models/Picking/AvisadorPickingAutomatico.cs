using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Exceptions;
using System;
using System.Net.Mail;

namespace NestoAPI.Models.Picking
{
    /// <summary>
    /// NestoAPI#361: avisa al almacén del resultado del picking AUTOMÁTICO de las 11h.
    ///
    /// El problema que resuelve: ese picking lo lanza una tarea del Task Scheduler, así que no hay
    /// nadie mirando una pantalla. Si no salía picking, el almacén no tenía forma de distinguir
    /// entre "no había nada que sacar", "ha fallado algo" y "la tarea ni se ha ejecutado", y
    /// acababa preguntando a Informática, que lo miraba en ELMAH.
    ///
    /// Al avisar de los DOS primeros casos, el silencio pasa a significar una sola cosa:
    ///   - Sale el picking .................. todo bien
    ///   - Correo "no había nada" ........... todo bien, no había trabajo
    ///   - Correo "ha fallado" .............. hay que actuar
    ///   - Ni picking ni correo ............. la tarea NO se ejecutó   (antes, indistinguible)
    ///
    /// En el picking interactivo no hace falta nada de esto: el usuario ve el error en pantalla.
    /// Por eso el aviso cuelga de un endpoint propio (api/Picking/Automatico) y no de un
    /// parámetro global, que podría acabar mandando correos en los pickings manuales.
    /// </summary>
    public class AvisadorPickingAutomatico
    {
        private readonly IServicioCorreoElectronico servicioCorreo;
        private readonly ILectorParametrosUsuario lectorParametros;

        public AvisadorPickingAutomatico(IServicioCorreoElectronico servicioCorreo,
            ILectorParametrosUsuario lectorParametros)
        {
            this.servicioCorreo = servicioCorreo;
            this.lectorParametros = lectorParametros;
        }

        /// <summary>
        /// "No había nada que sacar" es un resultado NORMAL, no un fallo: GestorPicking lo lanza
        /// como NestoBusinessException con el código PICKING_SIN_STOCK (400, y desde #361 fuera de
        /// ELMAH). Se distingue del resto para poder mandar un correo tranquilizador en vez de una
        /// alarma.
        /// </summary>
        internal static bool EsPickingSinTrabajo(Exception excepcion)
        {
            return excepcion is NestoBusinessException negocio
                && negocio.Context?.ErrorCode == Constantes.Picking.ERROR_SIN_STOCK;
        }

        internal static string AsuntoPara(Exception excepcion)
        {
            return EsPickingSinTrabajo(excepcion)
                ? "Picking automático: no había nada que sacar"
                : "AVISO: el picking automático ha fallado";
        }

        internal static string CuerpoPara(Exception excepcion, DateTime momento)
        {
            string hora = momento.ToString("dd/MM/yyyy HH:mm");

            if (EsPickingSinTrabajo(excepcion))
            {
                return "<p>El picking automático se ha ejecutado correctamente a las " + hora + ", " +
                       "pero <b>no había ningún pedido al que asignar picking</b> " +
                       "(sin stock suficiente o nada pendiente).</p>" +
                       "<p>No hay que hacer nada: es un resultado normal. " +
                       "Este correo se manda para que sepáis que la tarea SÍ se ha ejecutado.</p>";
            }

            return "<p>El picking automático de las " + hora + " <b>ha fallado</b> y no se ha sacado nada.</p>" +
                   "<p>Motivo:</p><blockquote>" + Escapar(excepcion?.Message) + "</blockquote>" +
                   "<p>Avisad a Informática si no sabéis resolverlo. " +
                   "El detalle técnico está registrado en el log.</p>";
        }

        /// <summary>
        /// El mensaje de la excepción es texto ajeno y va dentro del HTML del correo, así que hay
        /// que escaparlo. Se escapan SOLO los tres caracteres que pueden romper el marcado, y no
        /// se usa HttpUtility.HtmlEncode porque convierte además todas las tildes a entidades
        /// numéricas ("ubicaci&amp;#243;n") y deja el correo ilegible en el código fuente.
        /// El ampersand va primero, o se escaparían dos veces los que introducen los otros.
        /// </summary>
        internal static string Escapar(string texto)
        {
            return (texto ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        /// <summary>
        /// Destinatarios del aviso. Por defecto el almacén, pero se puede cambiar sin desplegar
        /// con el parámetro <c>CorreoAvisoPickingAutomatico</c> (varias direcciones separadas por
        /// punto y coma o coma).
        /// </summary>
        internal string[] Destinatarios()
        {
            string configurado = null;
            try
            {
                configurado = lectorParametros?.LeerParametro(
                    Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO,
                    Constantes.ParametrosUsuario.CORREO_AVISO_PICKING_AUTOMATICO);
            }
            catch
            {
                // Si el parámetro no se puede leer, el aviso NO se puede perder: se usa el almacén.
                configurado = null;
            }

            if (string.IsNullOrWhiteSpace(configurado))
            {
                return new[] { Constantes.Correos.ALMACEN };
            }

            return configurado.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// Manda el aviso. Nunca lanza: un fallo al avisar no puede tapar ni cambiar el resultado
        /// del picking, que es lo que de verdad importa.
        /// </summary>
        public void Avisar(Exception excepcion, DateTime momento)
        {
            try
            {
                using (MailMessage mail = new MailMessage())
                {
                    mail.From = new MailAddress("nesto@nuevavision.es");
                    foreach (string destinatario in Destinatarios())
                    {
                        mail.To.Add(new MailAddress(destinatario.Trim()));
                    }
                    mail.Subject = AsuntoPara(excepcion);
                    mail.Body = CuerpoPara(excepcion, momento);
                    mail.IsBodyHtml = true;
                    _ = servicioCorreo.EnviarCorreoSMTP(mail);
                }
            }
            catch
            {
                // Silencio deliberado: ver el comentario del método.
            }
        }
    }
}
