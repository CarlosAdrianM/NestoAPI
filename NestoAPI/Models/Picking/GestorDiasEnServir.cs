using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Configuration;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Models.Picking
{
    /// <summary>
    /// NestoAPI#362: los días que un cliente CIERRA (Clientes.DiasEnServir, char(5), herencia del
    /// Nesto viejo: 5 posiciones = lunes..viernes, '1' abre y '0' cierra; "01111" = cierra los
    /// lunes). El picking no debe sacar un pedido cuya entrega caería en un día cerrado: la
    /// agencia entrega ~24h después de la salida, así que el día de entrega es el siguiente
    /// laborable tras el día de picking.
    ///
    /// <para>El filtro vive en el PICKING y no en una fecha puesta al crear el pedido (opción A
    /// de la issue): el día de salida es dinámico (hora de corte, stock, festivos) y aquí se
    /// recalcula en cada pasada — el pedido "se guarda solo" y sale en la primera pasada cuya
    /// entrega caiga en día abierto. Para forzar una excepción sigue valiendo el override manual
    /// de Fecha_Entrega.</para>
    ///
    /// <para>Ante un dato raro (null, longitud distinta de 5, caracteres que no son 0/1) se
    /// considera ABIERTO: un dato defectuoso no debe dejar pedidos sin salir.</para>
    ///
    /// <para>NestoAPI#588: lo anterior es el plazo de la AGENCIA. Por nuestra ruta lo que se prepara hoy se entrega
    /// mañana. Si el pedido ya lleva ruta propia (16, AT) al sacar el picking, se mira ese día; si no, el de la
    /// agencia, como siempre (almacén suele decidir ruta o agencia después del picking). Al retirado que por nuestra
    /// ruta llegaría con el cliente abierto, el aviso le dice que con una ruta propia sale.</para>
    /// </summary>
    public static class GestorDiasEnServir
    {
        /// <summary>
        /// El día de ENTREGA estimado: el siguiente laborable tras el día de salida
        /// (fechaPicking), saltando findes y festivos igual que hace el propio picking.
        /// </summary>
        internal static DateTime CalcularDiaEntrega(DateTime fechaPicking, Func<DateTime, bool> esFestivo)
        {
            DateTime entrega = fechaPicking.Date.AddDays(1);
            while (esFestivo(entrega))
            {
                entrega = entrega.AddDays(1);
            }
            return entrega;
        }

        /// <summary>
        /// NestoAPI#588: el día de entrega si el pedido va por NUESTRA ruta. Lo que se prepara hoy se entrega mañana
        /// (el laborable siguiente al día en que se prepara), sea antes o después del corte. Por agencia, en cambio,
        /// lo que se prepara pasado el corte sale el día siguiente y se entrega el laborable de después
        /// (<see cref="CalcularDiaEntrega"/> desde fechaPicking). Antes del corte los dos coinciden; pasado el corte,
        /// el de nuestra ruta es un laborable antes (jueves por la tarde: viernes por ruta, lunes por agencia).
        /// </summary>
        /// <param name="hoy">El día en que se saca el picking (se prepara).</param>
        /// <param name="fechaPicking">El horizonte del picking (día de salida por agencia).</param>
        internal static DateTime CalcularDiaEntregaRutaPropia(DateTime hoy, DateTime fechaPicking, Func<DateTime, bool> esFestivo)
        {
            DateTime diaPreparacion = hoy.Date < fechaPicking.Date ? hoy.Date : fechaPicking.Date;
            return CalcularDiaEntrega(diaPreparacion, esFestivo);
        }

        /// <summary>
        /// NestoAPI#588: ¿la ruta del pedido es de las nuestras (16 «Ruta diaria», AT)? La misma lista que usa la
        /// facturación de rutas (<see cref="Facturas.RutaPropia"/>). Medido en producción (jul-oct/26): de los 68
        /// pedidos servidos con ruta 16 o AT, solo 2 tienen envío de agencia; de los 2.861 con 00 o FW, el 98 % sí.
        /// Una ruta vacía, de agencia o cualquier otra cuenta como agencia, que es lo que se hacía hasta ahora.
        /// </summary>
        internal static bool EsRutaPropia(string ruta)
        {
            return RUTA_PROPIA.ContieneRuta(ruta);
        }

        private static readonly Facturas.RutaPropia RUTA_PROPIA = new Facturas.RutaPropia();

        /// <summary>NestoAPI#471: 5 caracteres, cada uno '0' o '1'.</summary>
        public static bool EsFormatoValido(string diasEnServir)
        {
            string dias = diasEnServir?.Trim();
            return dias != null && dias.Length == 5 && dias.All(c => c == '0' || c == '1');
        }

        /// <summary>
        /// NestoAPI#471: el valor que debe quedar en la ficha al guardar. Null o vacío = no tocar
        /// (los llamantes que aún no envían el campo no deben blanquearlo); si no había nada, el
        /// valor por defecto (abre todos los días). Un valor mal formado no se guarda: se rechaza.
        /// </summary>
        public static string AplicarCambio(string actual, string nuevo)
        {
            if (string.IsNullOrWhiteSpace(nuevo))
            {
                return string.IsNullOrWhiteSpace(actual) ? Constantes.Clientes.DIAS_EN_SERVIR_POR_DEFECTO : actual;
            }
            if (!EsFormatoValido(nuevo))
            {
                throw new ValidationException($"Los días de servir deben ser 5 posiciones (lunes a viernes) con 0 o 1: '{nuevo}' no vale.");
            }
            return nuevo.Trim();
        }

        internal static bool EstaAbierto(string diasEnServir, DateTime diaEntrega)
        {
            string dias = diasEnServir?.Trim();
            if (dias == null || dias.Length != 5 || dias.Any(c => c != '0' && c != '1'))
            {
                return true;
            }
            // "00000" no es un cliente que cierra todos los días: es un dato roto (medido en
            // producción el 01/09/26: 57 fichas, 45 vivas, todas recibiendo pedidos con
            // normalidad). Tomarlo en serio dejaría sus pedidos sin salir PARA SIEMPRE.
            if (!dias.Contains('1'))
            {
                return true;
            }

            switch (diaEntrega.DayOfWeek)
            {
                case DayOfWeek.Monday: return dias[0] == '1';
                case DayOfWeek.Tuesday: return dias[1] == '1';
                case DayOfWeek.Wednesday: return dias[2] == '1';
                case DayOfWeek.Thursday: return dias[3] == '1';
                case DayOfWeek.Friday: return dias[4] == '1';
                default:
                    // La entrega nunca cae en finde (CalcularDiaEntrega los salta), y el campo
                    // solo modela lunes..viernes: si llegara, mejor abierto que retener.
                    return true;
            }
        }

        /// <summary>
        /// Quita del picking los pedidos cuya entrega caería en un día que el cliente cierra
        /// (vaciándoles las líneas, igual que las demás reglas de "no debe salir": el pedido se
        /// queda pendiente y se reevalúa en la siguiente pasada). Devuelve los retirados para el
        /// aviso: un pedido que no sale sin decir por qué parece un cuelgue.
        /// </summary>
        /// <param name="ignorarCierre">Picking de UN pedido en el que el usuario ha confirmado «¿Aún así quieres
        /// asignarle picking?» (01/10/26): no se retira nada.</param>
        internal static List<PedidoPicking> RetirarPedidosDeClientesCerrados(
            List<PedidoPicking> candidatos, DateTime diaEntrega, bool ignorarCierre = false)
        {
            return RetirarPedidosDeClientesCerrados(candidatos, diaEntrega, diaEntrega, ignorarCierre);
        }

        /// <summary>
        /// NestoAPI#588: el día de entrega depende de por dónde va el pedido. Si su ruta ya es propia (16, AT), se mira
        /// el día de nuestra ruta; si no (lo normal al sacar el picking: almacén decide ruta o agencia después), el de
        /// la agencia, como hasta ahora. A los retirados que van por agencia pero cuyo cliente abre el día de nuestra
        /// ruta se les deja ese día en <see cref="PedidoPicking.DiaEntregaSiVaPorNuestraRuta"/>, para decirlo en el
        /// aviso: con una ruta propia en el pedido, el siguiente picking lo saca.
        /// </summary>
        internal static List<PedidoPicking> RetirarPedidosDeClientesCerrados(
            List<PedidoPicking> candidatos, DateTime diaEntregaAgencia, DateTime diaEntregaRutaPropia, bool ignorarCierre = false)
        {
            if (ignorarCierre)
            {
                return new List<PedidoPicking>();
            }
            List<PedidoPicking> retirados = new List<PedidoPicking>();
            foreach (PedidoPicking pedido in candidatos.Where(p => p.Lineas != null && p.Lineas.Count > 0))
            {
                bool porNuestraRuta = EsRutaPropia(pedido.Ruta);
                DateTime diaEntrega = porNuestraRuta ? diaEntregaRutaPropia : diaEntregaAgencia;
                if (EstaAbierto(pedido.DiasEnServir, diaEntrega))
                {
                    continue;
                }
                pedido.DiaEntregaRetiradoPorCierre = diaEntrega;
                pedido.DiaEntregaSiVaPorNuestraRuta = !porNuestraRuta && diaEntregaRutaPropia != diaEntregaAgencia
                    && EstaAbierto(pedido.DiasEnServir, diaEntregaRutaPropia)
                    ? diaEntregaRutaPropia
                    : (DateTime?)null;
                pedido.Lineas.Clear();
                retirados.Add(pedido);
            }
            return retirados;
        }

        /// <summary>NestoAPI#588: el día con el que se retiró cada pedido (el del picking si no lo trae).</summary>
        private static DateTime DiaDe(PedidoPicking pedido, DateTime diaPorDefecto)
        {
            return pedido.DiaEntregaRetiradoPorCierre ?? diaPorDefecto;
        }

        /// <summary>
        /// NestoAPI#588: la pista para almacén cuando algún pedido retirado se podría entregar por nuestra ruta en un día
        /// que el cliente abre. Vacío si no hay ninguno.
        /// </summary>
        internal static string PistaRutaPropia(IEnumerable<PedidoPicking> retirados)
        {
            List<PedidoPicking> conPista = (retirados ?? Enumerable.Empty<PedidoPicking>())
                .Where(p => p.DiaEntregaSiVaPorNuestraRuta.HasValue)
                .ToList();
            if (conPista.Count == 0)
            {
                return string.Empty;
            }
            var cultura = new System.Globalization.CultureInfo("es-ES");
            IEnumerable<string> partes = conPista
                .GroupBy(p => p.DiaEntregaSiVaPorNuestraRuta.Value)
                .Select(g =>
                {
                    string ids = string.Join(", ", g.Select(p => p.Id).Distinct());
                    return $"{ids} el {g.Key.ToString("dddd", cultura)} {g.Key:dd/MM/yyyy}";
                });
            string pedidos = conPista.Select(p => p.Id).Distinct().Count() == 1 ? "el pedido" : "los pedidos";
            return $"Ese día es el de la agencia. Si va por nuestra ruta, se entregaría antes, con el cliente abierto ({pedidos} {string.Join("; ", partes)}): " +
                "pon en el pedido una ruta propia (16 o AT) y vuelve a sacarle el picking.";
        }

        /// <summary>
        /// El error de negocio cuando el picking se queda sin nada. Si lo que lo ha vaciado es el cierre del cliente,
        /// lo dice (caso real 01/10/26, Alfredo, cliente 5057 «LOS LUNES CIERRA»: salía «No hay stock suficiente…»
        /// con stock de sobra y no se entendía nada).
        /// </summary>
        internal static Infraestructure.Exceptions.NestoBusinessException ErrorSinPicking(List<PedidoPicking> retiradosPorCierre, DateTime diaEntrega)
        {
            if (retiradosPorCierre == null || retiradosPorCierre.Count == 0)
            {
                return new Infraestructure.Exceptions.NestoBusinessException(
                    "No hay stock suficiente para asignar picking a ninguna línea",
                    new Infraestructure.Exceptions.ErrorContext { ErrorCode = Constantes.Picking.ERROR_SIN_STOCK })
                {
                    IsWarning = true
                };
            }

            var cultura = new System.Globalization.CultureInfo("es-ES");
            string Dia(DateTime d) => $"{d.ToString("dddd", cultura)} {d:dd/MM/yyyy}";
            List<DateTime> dias = retiradosPorCierre.Select(p => DiaDe(p, diaEntrega)).Distinct().OrderBy(d => d).ToList();
            string clientes = string.Join(", ", retiradosPorCierre.Select(p => p.Cliente?.Trim()).Where(c => !string.IsNullOrEmpty(c)).Distinct());
            string mensaje;
            if (dias.Count == 1)
            {
                string dia = Dia(dias[0]);
                string pedidos = string.Join(", ", retiradosPorCierre.Select(p => p.Id).Distinct());
                mensaje = retiradosPorCierre.Select(p => p.Id).Distinct().Count() == 1
                    ? $"El pedido {pedidos} no sale: la entrega de este picking sería el {dia} y el cliente {clientes} cierra ese día. " +
                      "Saldrá solo en el primer picking cuya entrega caiga en un día que abra."
                    : $"Los pedidos {pedidos} no salen: la entrega de este picking sería el {dia} y el cliente ({clientes}) cierra ese día. " +
                      "Saldrán solos en el primer picking cuya entrega caiga en un día que abra.";
            }
            else
            {
                // NestoAPI#588: los que van por nuestra ruta y los que van por agencia se entregan en días distintos
                string detalle = string.Join("; ", retiradosPorCierre
                    .GroupBy(p => DiaDe(p, diaEntrega))
                    .OrderBy(g => g.Key)
                    .Select(g => $"{string.Join(", ", g.Select(p => p.Id).Distinct())} el {Dia(g.Key)}"));
                mensaje = $"Los pedidos no salen porque el cliente ({clientes}) cierra el día en que se entregarían ({detalle}). " +
                    "Saldrán solos en el primer picking cuya entrega caiga en un día que abra.";
            }
            string pista = PistaRutaPropia(retiradosPorCierre);
            if (pista.Length > 0)
            {
                mensaje += " " + pista;
            }
            return new Infraestructure.Exceptions.NestoBusinessException(mensaje,
                new Infraestructure.Exceptions.ErrorContext { ErrorCode = Constantes.Picking.ERROR_CLIENTE_CERRADO })
            {
                IsWarning = true
            };
        }

        /// <summary>
        /// Los pedidos retirados de los que todavía no se ha avisado para ESE día de entrega. Un
        /// pedido que el cliente no puede recibir el lunes se retira en todas las pasadas del
        /// picking hasta que cambia el día de entrega, y avisar en cada pasada es spam (10/09/26:
        /// almacén se quejó de que le llegaba un correo por cada picking). Se avisa una vez por
        /// pedido y día de entrega; el registro es el de la propia app (se pierde al reciclar el
        /// pool, y entonces como mucho se repite un aviso).
        /// </summary>
        internal static List<PedidoPicking> PendientesDeAvisar(List<PedidoPicking> pedidosRetirados, DateTime diaEntrega, IDictionary<string, DateTime> yaAvisados)
        {
            List<PedidoPicking> nuevos = new List<PedidoPicking>();
            foreach (PedidoPicking pedido in pedidosRetirados)
            {
                string clave = $"{pedido.Id}|{DiaDe(pedido, diaEntrega):yyyyMMdd}";
                if (yaAvisados.ContainsKey(clave))
                {
                    continue;
                }
                yaAvisados[clave] = DateTime.Now;
                nuevos.Add(pedido);
            }
            // Que no crezca sin fin: lo de hace más de una semana ya no se va a repetir
            foreach (string clave in yaAvisados.Where(a => a.Value < DateTime.Now.AddDays(-7)).Select(a => a.Key).ToList())
            {
                yaAvisados.Remove(clave);
            }
            return nuevos;
        }

        private static readonly ConcurrentDictionary<string, DateTime> avisosEnviados = new ConcurrentDictionary<string, DateTime>();

        /// <summary>
        /// Aviso al usuario de cada pedido con ALMACÉN en copia (son ellos quienes echan en falta
        /// el pedido en el picking; administración no pinta nada aquí), mismo patrón que el correo
        /// de retenidos por prepago. Una sola vez por pedido y día de entrega. Nunca lanza: un
        /// fallo de correo no debe romper el picking.
        /// </summary>
        public static void EnviarCorreo(List<PedidoPicking> pedidosRetirados, DateTime diaEntrega)
        {
            if (pedidosRetirados == null || pedidosRetirados.Count == 0)
            {
                return;
            }
            pedidosRetirados = PendientesDeAvisar(pedidosRetirados, diaEntrega, avisosEnviados);
            if (pedidosRetirados.Count == 0)
            {
                return;
            }

            try
            {
                MailMessage mail = new MailMessage();
                SmtpClient client = new SmtpClient
                {
                    Port = 587,
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new System.Net.NetworkCredential("nesto@nuevavision.es",
                        ConfigurationManager.AppSettings["office365password"]),
                    Host = "smtp.office365.com"
                };
                mail.From = new MailAddress("nesto@nuevavision.es");
                foreach (string correoUsuario in pedidosRetirados.Select(p => p.CorreoUsuarioPedido).Distinct())
                {
                    mail.To.Add(new MailAddress(correoUsuario));
                }
                mail.CC.Add(new MailAddress(Constantes.Correos.ALMACEN));
                mail.Subject = "Pedidos sin picking: el cliente cierra el día de la entrega";
                // NestoAPI#588: por nuestra ruta y por agencia el día de entrega puede ser distinto; una tabla por día
                mail.Body = string.Concat(pedidosRetirados
                    .GroupBy(p => DiaDe(p, diaEntrega))
                    .OrderBy(g => g.Key)
                    .Select(g => GenerarCuerpo(g.ToList(), g.Key)));
                mail.IsBodyHtml = true;
                try
                {
                    client.Send(mail);
                }
                catch
                {
                    _ = Task.Delay(2000);
                    client.Send(mail);
                }
            }
            catch
            {
                // El aviso es cortesía; el picking ya ha hecho lo correcto.
            }
        }

        /// <summary>
        /// NestoAPI#497: el cuerpo lo leen personas que no saben cómo se guarda el dato. Nada de
        /// "01111": una columna por día con Abierto/Cerrado, el día de la entrega resaltado, y el
        /// motivo en una frase con el día de la semana en palabras.
        /// </summary>
        internal static string GenerarCuerpo(List<PedidoPicking> pedidos, DateTime diaEntrega)
        {
            System.Globalization.CultureInfo castellano = new System.Globalization.CultureInfo("es-ES");
            string diaSemana = diaEntrega.ToString("dddd", castellano);
            StringBuilder s = new StringBuilder();
            _ = s.Append("<p>Estos pedidos no han cogido picking porque se entregarían el <strong>");
            _ = s.Append(diaEntrega.ToString("dddd d 'de' MMMM", castellano));
            _ = s.Append($"</strong>, y el cliente tiene el <strong>{diaSemana}</strong> marcado como <strong>cerrado</strong> en su ficha ");
            _ = s.Append("(días de servir). Saldrán solos en la primera pasada cuya entrega caiga en un día abierto; ");
            _ = s.Append("para forzar la salida, poner una fecha de entrega concreta en el pedido.</p>");
            _ = s.Append("<table border='1' cellpadding='4' cellspacing='0'><tr><th>Pedido</th><th>Cliente</th>");
            foreach (string dia in NOMBRES_DIAS)
            {
                _ = s.Append(dia.Equals(diaSemana, StringComparison.OrdinalIgnoreCase)
                    ? $"<th style='background-color:#fde2e2'>{dia}</th>"
                    : $"<th>{dia}</th>");
            }
            _ = s.Append("</tr>");
            foreach (PedidoPicking pedido in pedidos)
            {
                _ = s.Append($"<tr><td>{pedido.Id}</td><td>{pedido.Cliente?.Trim()}</td>");
                _ = s.Append(CeldasDias(pedido.DiasEnServir, diaEntrega));
                _ = s.Append("</tr>");
            }
            _ = s.Append("</table>");
            // NestoAPI#588
            string pista = PistaRutaPropia(pedidos);
            if (pista.Length > 0)
            {
                _ = s.Append($"<p><strong>{System.Net.WebUtility.HtmlEncode(pista)}</strong></p>");
            }
            _ = s.Append("<p style='color:#666;font-size:90%'>Abierto = el cliente recibe ese día; Cerrado = no. ");
            _ = s.Append("Se cambia en la ficha del cliente (días de servir).</p>");
            return s.ToString();
        }

        private static readonly string[] NOMBRES_DIAS = { "lunes", "martes", "miércoles", "jueves", "viernes" };

        /// <summary>
        /// Las cinco celdas lunes..viernes de un cliente, en palabras. La del día de la entrega va
        /// resaltada. Un dato que no es de 5 posiciones 0/1 sale como "Sin dato" (GestorDiasEnServir
        /// lo trata como abierto, así que un pedido con ese dato no llega a este correo).
        /// </summary>
        internal static string CeldasDias(string diasEnServir, DateTime diaEntrega)
        {
            string dias = diasEnServir?.Trim();
            bool valido = dias != null && dias.Length == 5 && dias.All(c => c == '0' || c == '1');
            StringBuilder s = new StringBuilder();
            for (int i = 0; i < 5; i++)
            {
                string texto = !valido ? "Sin dato" : (dias[i] == '1' ? "Abierto" : "Cerrado");
                bool esElDiaDeEntrega = (int)diaEntrega.DayOfWeek == i + 1;   // Monday = 1
                _ = s.Append(esElDiaDeEntrega
                    ? $"<td style='background-color:#fde2e2'><strong>{texto}</strong></td>"
                    : $"<td>{texto}</td>");
            }
            return s.ToString();
        }
    }
}
