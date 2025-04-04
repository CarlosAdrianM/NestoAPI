using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Models.Rapports;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public class SeguimientosClientesController : ApiController
    {
        private readonly NVEntities db = new NVEntities();
        private readonly IServicioCorreoElectronico _servicioCorreoElectronico;

        // Carlos 13/03/17: lo pongo para desactivar el Lazy Loading
        public SeguimientosClientesController()
        {
            db.Configuration.LazyLoadingEnabled = false;
            _servicioCorreoElectronico = new ServicioCorreoElectronico();
        }

        // GET: api/SeguimientosClientes
        public IQueryable<SeguimientoClienteDTO> GetSeguimientosClientes(string empresa, string cliente, string contacto)
        {
            DateTime fechaDesde = DateTime.Today.AddYears(-3);
            return db.SeguimientosClientes
                .Include(s => s.Cliente)
                .Where(s => s.Empresa == empresa && s.Número == cliente && s.Contacto == contacto && s.Fecha >= fechaDesde)
                .Select(s => new SeguimientoClienteDTO
                {
                    Aparatos = s.Aparatos,
                    Aviso = s.Aviso,
                    Cliente = s.Número.Trim(),
                    ClienteNuevo = s.ClienteNuevo,
                    Comentarios = s.Comentarios,
                    Contacto = s.Contacto.Trim(),
                    Direccion = s.Cliente.Dirección,
                    Empresa = s.Empresa.Trim(),
                    Estado = (SeguimientoClienteDTO.EstadoSeguimientoDTO)s.Estado,
                    Fecha = s.Fecha,
                    GestionAparatos = s.GestiónAparatos,
                    Id = s.NºOrden,
                    Nombre = s.Cliente.Nombre,
                    Pedido = s.Pedido,
                    PrimeraVisita = s.PrimeraVisita,
                    Tipo = s.Tipo.Trim(),
                    Usuario = s.Usuario,
                    Vendedor = s.Vendedor
                })
                .OrderByDescending(s => s.Id);
        }

        // GET: api/SeguimientosClientes
        public IQueryable<SeguimientoClienteDTO> GetSeguimientosClientes(string vendedor, DateTime fecha)
        {
            DateTime fechaDesde = new DateTime(fecha.Year, fecha.Month, fecha.Day);
            DateTime fechaHasta = fechaDesde.AddDays(1);

            IQueryable<SeguimientoCliente> seguimientos;

            if (vendedor != null)
            {
                seguimientos = vendedor.Length <= 3
                    ? db.SeguimientosClientes.Where(s => s.Vendedor == vendedor)
                    : db.SeguimientosClientes.Where(s => s.Usuario == vendedor);
            }
            else
            {
                seguimientos = db.SeguimientosClientes;
            }


            return seguimientos
                .Include(s => s.Cliente)
                .Where(s => s.Fecha >= fechaDesde && s.Fecha < fechaHasta)
                .Select(s => new SeguimientoClienteDTO
                {
                    Aparatos = s.Aparatos,
                    Aviso = s.Aviso,
                    Cliente = s.Número,
                    ClienteNuevo = s.ClienteNuevo,
                    Comentarios = s.Comentarios,
                    Contacto = s.Contacto,
                    Direccion = s.Cliente.Dirección,
                    Empresa = s.Empresa,
                    Estado = (SeguimientoClienteDTO.EstadoSeguimientoDTO)s.Estado,
                    Fecha = s.Fecha,
                    GestionAparatos = s.GestiónAparatos,
                    Id = s.NºOrden,
                    Nombre = s.Cliente.Nombre,
                    Pedido = s.Pedido,
                    PrimeraVisita = s.PrimeraVisita,
                    Tipo = s.Tipo.Trim(),
                    Usuario = s.Usuario,
                    Vendedor = s.Vendedor
                })
                .OrderByDescending(s => s.Id);
        }

        // GET: api/SeguimientosClientes
        public IQueryable<SeguimientoClienteDTO> GetSeguimientosClientes(string vendedor, string filtro)
        {
            DateTime fechaDesde = DateTime.Today.AddYears(-3);
            IQueryable<SeguimientoCliente> resultado = db.SeguimientosClientes
                .Include(s => s.Cliente)
                .Where(s => s.Comentarios.Contains(filtro));

            if (!string.IsNullOrEmpty(vendedor))
            {
                IServicioVendedores _servicioVendedores = new ServicioVendedores(); // habría que inyectarlo
                List<string> listaVendedores = _servicioVendedores.VendedoresEquipo(Constantes.Empresas.EMPRESA_POR_DEFECTO, vendedor).GetAwaiter().GetResult().Select(l => l.vendedor).ToList();
                resultado = resultado.Include(s => s.Cliente).Where(s => listaVendedores.Contains(s.Cliente.Vendedor));
            }

            return resultado.Select(s => new SeguimientoClienteDTO
            {
                Aparatos = s.Aparatos,
                Aviso = s.Aviso,
                Cliente = s.Número,
                ClienteNuevo = s.ClienteNuevo,
                Comentarios = s.Comentarios,
                Contacto = s.Contacto,
                Direccion = s.Cliente.Dirección,
                Empresa = s.Empresa,
                Estado = (SeguimientoClienteDTO.EstadoSeguimientoDTO)s.Estado,
                Fecha = s.Fecha,
                GestionAparatos = s.GestiónAparatos,
                Id = s.NºOrden,
                Nombre = s.Cliente.Nombre,
                Pedido = s.Pedido,
                PrimeraVisita = s.PrimeraVisita,
                Tipo = s.Tipo.Trim(),
                Usuario = s.Usuario,
                Vendedor = s.Vendedor
            }).OrderByDescending(s => s.Id);
        }

        [HttpGet]
        [Route("api/SeguimientosClientes/GetCodigosPostalesSinVisitar")]
        // GET: api/Clientes/5
        [ResponseType(typeof(ICollection<CodigoPostalSeguimientoLookup>))]
        public async Task<IHttpActionResult> GetCodigosPostalesSinVisitar(string vendedor, DateTime fechaDesde, DateTime fechaHasta)
        {
            GestorRapports gestor = new GestorRapports();
            ICollection<CodigoPostalSeguimientoLookup> respuesta = await gestor.CodigosPostalesSinVisitar(vendedor, fechaDesde, fechaHasta);
            return Ok(respuesta);
        }


        [HttpGet]
        [Route("api/SeguimientosClientes/GetClientesSinVisitar")]
        // GET: api/Clientes/5
        [ResponseType(typeof(ICollection<ClienteSeguimientoLookup>))]
        public async Task<IHttpActionResult> GetClientesSinVisitar(string vendedor, string codigoPostal, DateTime fechaDesde, DateTime fechaHasta)
        {
            GestorRapports gestor = new GestorRapports();
            ICollection<ClienteSeguimientoLookup> respuesta = await gestor.ClientesSinVisitar(vendedor, codigoPostal, fechaDesde, fechaHasta);
            return Ok(respuesta);
        }

        [HttpGet]
        [Route("api/SeguimientosClientes/Resumen")]
        public async Task<IHttpActionResult> GetResumenSeguimientosClientes(string empresa, string cliente, string contacto)
        {
            // Obtiene los 100 comentarios y fechas de los seguimientos más recientes
            var seguimientos = db.SeguimientosClientes
                .Where(s => s.Empresa == empresa && s.Número == cliente && s.Contacto == contacto)
                .Select(s => new { s.Fecha, s.Comentarios, s.Pedido })
                .OrderByDescending(s => s.Fecha)  // Ordenar por fecha descendente
                .Take(100)                        // Tomar solo los 100 más recientes
                .OrderBy(s => s.Fecha)            // Reordenar por fecha ascendente para mantener el orden cronológico
                .ToList();

            if (!seguimientos.Any())
            {
                return NotFound();
            }

            // Crea el texto de entrada para OpenAI, incluyendo la fecha con cada comentario
            string textoEntrada = "Resumen de seguimientos de un cliente. Los siguientes son los comentarios organizados por fecha:\n\n";
            foreach (var seguimiento in seguimientos)
            {
                string pedidoTexto = seguimiento.Pedido ? "Sí" : "No";
                textoEntrada += $"Fecha: {seguimiento.Fecha:yyyy-MM-dd HH:mm:ss}\nComentario: {seguimiento.Comentarios}\nTerminó en pedido: {pedidoTexto}\n\n";
            }

            // Llama a OpenAI para obtener el resumen
            string resumen = await GenerarResumenOpenAIAsync(textoEntrada);

            return string.IsNullOrEmpty(resumen) ? StatusCode(HttpStatusCode.InternalServerError) : (IHttpActionResult)Ok(new { Resumen = resumen });
        }

        [HttpGet]
        [Route("api/SeguimientosClientes/Resumen")]
        public async Task<IHttpActionResult> GetResumenSeguimientosClientes(string empresa, DateTime fecha)
        {
            string[] vendedoresPresenciales = db.EquiposVentas.Where(v => v.Superior == Constantes.Vendedores.JEFE_DE_VENTAS).Select(v => v.Vendedor).ToArray();
            string resumenPresenciales = await EnviarCorreoResumenRapportsDia(empresa, fecha, vendedoresPresenciales, Constantes.Correos.JEFE_VENTAS, false);
            string resumenResto = await EnviarCorreoResumenRapportsDia(empresa, fecha, vendedoresPresenciales, Constantes.Correos.CORREO_DIRECCION, true);
            return Ok(new { Resumen = resumenPresenciales + "\n" + resumenResto });
        }

        [HttpGet]
        [Route("api/SeguimientosClientes/ResumenSemanal")]
        public async Task<IHttpActionResult> GetResumenSemanalSeguimientosClientes(string empresa, DateTime fecha)
        {
            DateTime fechaSinHora = new DateTime(fecha.Year, fecha.Month, fecha.Day);
            DateTime fechaSemanaAnterior = fechaSinHora.AddDays(-7);
            string[] vendedoresPresenciales = db.EquiposVentas.Where(v => v.Superior == Constantes.Vendedores.JEFE_DE_VENTAS).Select(v => v.Vendedor.Trim()).ToArray();
            string resumenConjunto = string.Empty;
            string[] vendedoresConRapport = db.SeguimientosClientes
                .Where(s => s.Empresa == empresa && s.Fecha >= fechaSemanaAnterior && s.Fecha < fechaSinHora && s.Vendedor != null)
                .Select(s => s.Vendedor.Trim())
                .Distinct()
                .ToArray();

            foreach (string vendedor in vendedoresConRapport)
            {
                string correo = db.Vendedores.SingleOrDefault(v => v.Empresa == empresa && v.Número == vendedor).Mail.Trim();
                string copia = vendedoresPresenciales.Contains(vendedor) ? Constantes.Correos.JEFE_VENTAS : Constantes.Correos.CORREO_DIRECCION;
                string resumen = await EnviarCorreoResumenRapportsSemana(empresa, fechaSemanaAnterior, fechaSinHora, vendedor, correo, copia);
                resumenConjunto += resumen + "\n";
            }

            return Ok(new { Resumen = resumenConjunto });
        }

        private async Task<string> EnviarCorreoResumenRapportsDia(string empresa, DateTime fecha, string[] vendedores, string correo, bool resto)
        {
            // Resto discrimina entre si vendedores[] son los que enviamos o los que no enviamos

            DateTime fechaSinHora = new DateTime(fecha.Year, fecha.Month, fecha.Day);
            DateTime fechaDiaSiguiente = fechaSinHora.AddDays(1);

            var seguimientos = db.SeguimientosClientes
                .Where(s => s.Empresa == empresa &&
                            s.Fecha >= fechaSinHora &&
                            s.Fecha < fechaDiaSiguiente &&
                            s.Estado == 0 &&
                            s.Número != null &&
                            (resto ? !vendedores.Contains(s.Vendedor) : vendedores.Contains(s.Vendedor)))
                .Select(s => new { s.Vendedor, s.Número, s.Contacto, s.Comentarios, s.Pedido, s.Tipo })
                .ToList();


            if (!seguimientos.Any())
            {
                return string.Empty;
            }

            // Crea el texto de entrada para OpenAI, incluyendo la fecha con cada comentario
            string textoEntrada = $"Resumen de seguimientos del día {fecha}. Los siguientes son los comentarios:\n\n";
            foreach (var seguimiento in seguimientos.Where(s => s.Comentarios?.Length >= 10))
            {
                string pedidoTexto = seguimiento.Pedido ? "Sí" : "No";
                switch (seguimiento.Tipo.Trim())
                {
                    case "V":
                        textoEntrada += "Tipo: Visita\n";
                        break;
                    case "T":
                        textoEntrada += "Tipo: Teléfono\n";
                        break;
                    case "W":
                        textoEntrada += "Tipo: WhatsApp\n";
                        break;
                    default:
                        textoEntrada += "Tipo: Desconocido\n";
                        break;
                }
                textoEntrada += $"Vendedor: {seguimiento.Vendedor}\nCliente: {seguimiento.Número.Trim()}/{seguimiento.Contacto.Trim()} \nComentario: {seguimiento.Comentarios.Trim()}\nTerminó en pedido: {pedidoTexto}\n\n";
            }

            // Llama a OpenAI para obtener el resumen
            string resumen = await GenerarResumenFechaOpenAIAsync(textoEntrada);

            if (string.IsNullOrEmpty(resumen))
            {
                return string.Empty;
            }

            string grupo = resto ? "resto" : "presenciales";

            MailMessage mail = new MailMessage
            {
                From = new MailAddress("nesto@nuevavision.es")
            };
            mail.To.Add(correo);
            mail.CC.Add(Constantes.Correos.CORREO_DIRECCION);
            mail.Subject = $"Resumen de seguimientos día {fechaSinHora.ToShortDateString()} ({grupo})";
            mail.Body = resumen;
            mail.IsBodyHtml = true;
            _ = _servicioCorreoElectronico.EnviarCorreoSMTP(mail);

            return resumen;
        }

        private async Task<string> EnviarCorreoResumenRapportsSemana(string empresa, DateTime fechaInicio, DateTime fechaFin, string vendedor, string correo, string copia)
        {
            var seguimientos = db.SeguimientosClientes
                .Where(s => s.Empresa == empresa &&
                            s.Fecha >= fechaInicio &&
                            s.Fecha < fechaFin &&
                            s.Número != null &&
                            s.Vendedor == vendedor)
                .Select(s => new { s.Vendedor, s.Número, s.Contacto, s.Comentarios, s.Fecha_Modificación })
                .OrderBy(s => s.Vendedor)
                .ThenBy(s => s.Fecha_Modificación)
                .ToList();


            if (!seguimientos.Any())
            {
                return string.Empty;
            }

            // Crea el texto de entrada para OpenAI, incluyendo la fecha con cada comentario
            string textoEntrada = $"Resumen de seguimientos del {fechaInicio} al {fechaFin}. Los siguientes son los comentarios:\n\n";
            foreach (var seguimiento in seguimientos.Where(s => s.Comentarios?.Length >= 10))
            {
                textoEntrada += $"Vendedor: {seguimiento.Vendedor}\nCliente: {seguimiento.Número.Trim()}/{seguimiento.Contacto.Trim()} \nComentario: {seguimiento.Comentarios.Trim()}\nFecha y hora: {seguimiento.Fecha_Modificación}\n\n";
            }

            // Llama a OpenAI para obtener el resumen
            string resumen = await GenerarResumenSemanaOpenAIAsync(textoEntrada);

            if (string.IsNullOrEmpty(resumen))
            {
                return string.Empty;
            }

            MailMessage mail = new MailMessage
            {
                From = new MailAddress("nesto@nuevavision.es")
            };
            mail.To.Add(correo);
            mail.CC.Add(copia);
            if (copia != Constantes.Correos.CORREO_DIRECCION)
            {
                mail.CC.Add(Constantes.Correos.CORREO_DIRECCION);
            }
            mail.Subject = $"Resumen de seguimientos semanal del {fechaInicio.ToShortDateString()} al {fechaFin.ToShortDateString()}";
            mail.Body = resumen;
            mail.IsBodyHtml = true;
            _ = _servicioCorreoElectronico.EnviarCorreoSMTP(mail);

            return resumen;
        }

        private async Task<string> GenerarResumenOpenAIAsync(string textoEntrada)
        {
            string apiKey = ConfigurationManager.AppSettings["OpenAIKey"];
            string endpoint = "https://api.openai.com/v1/chat/completions";



            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            HttpClientHandler handler = new HttpClientHandler
            {
                UseProxy = false // Cambiar según sea necesario
            };

            using (HttpClient httpClient = new HttpClient(handler))
            {
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                var payload = new
                {
                    model = "gpt-3.5-turbo",
                    messages = new[]
                    {
                        new {
                            role = "system",
                            content = "Eres un asistente que analiza comentarios de clientes y extrae información comercial útil. Identifica detalles como las marcas que utiliza el cliente, los tratamientos que realiza, los productos que le interesan, y cualquier otra preferencia relevante. Organiza el resumen de forma clara para que sea útil en una interacción comercial."
                        },
                        new {
                            role = "user",
                            content = textoEntrada
                        }
                    },
                    max_tokens = 500,
                    temperature = 0.4
                };



                StringContent content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                HttpResponseMessage response = await httpClient.PostAsync(endpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    string responseString = await response.Content.ReadAsStringAsync();
                    dynamic responseJson = JsonConvert.DeserializeObject<dynamic>(responseString);
                    return responseJson?.choices[0]?.message?.content?.ToString();
                }

                return null;
            }
        }
        private async Task<string> GenerarResumenFechaOpenAIAsync(string textoEntrada)
        {
            string apiKey = ConfigurationManager.AppSettings["OpenAIKey"];
            string endpoint = "https://api.openai.com/v1/chat/completions";



            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            HttpClientHandler handler = new HttpClientHandler
            {
                UseProxy = false // Cambiar según sea necesario
            };

            using (HttpClient httpClient = new HttpClient(handler))
            {
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                var payload = new
                {
                    model = "gpt-3.5-turbo",
                    messages = new[]
                    {
                        new {
                            role = "system",
                            content = "Eres un experto en ventas a nivel mundial, como Og Mandino o Chet Holmes, que analiza los comentarios de clientes que los vendedores de una empresa distribuidora de productos de estética y peluquería meten a su sistema informático para informar y ayudar al jefe de ventas. En primer lugar lee todos y cada uno de los comentarios y muestra al jefe de ventas la información que extraigas de esos comentarios que consideres más importante intentando que haya comentarios de casi todos los vendedores (indicando el cliente y asegúrandote que tu comentario coincide con el cliente que muestras). A continuación identifica posibles ventas que necesitan ayuda para ser cerradas y comentarios más interesantes a nivel comercial, con al menos un comentario por cada vendedor (asegúrate que el cliente coincide con el comentario). Añade tendencias que se repiten en varios clientes. Añade ambién consejos para que el jefe de ventas trasmita a algunos vendedores determinados (indicando para qué vendedor es el consejo y explicando qué clientes han provocado que se le de ese consejo). Añade también productos, eventos, cursos o marcas que se repitan entre varios vendedores. El resultado lo devuelves formateado en HTML para que sea visualmente atractivo y debe ser una lectura de no menos de dos minutos."
                        },
                        new {
                            role = "user",
                            content = textoEntrada
                        }
                    },
                    max_tokens = 3000,
                    temperature = 0.6
                };



                StringContent content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                HttpResponseMessage response = await httpClient.PostAsync(endpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    string responseString = await response.Content.ReadAsStringAsync();
                    dynamic responseJson = JsonConvert.DeserializeObject<dynamic>(responseString);
                    return responseJson?.choices[0]?.message?.content?.ToString();
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Error: {response.StatusCode}");
                    Console.WriteLine($"Detalles del error: {errorContent}");
                }

                return null;
            }
        }
        private async Task<string> GenerarResumenSemanaOpenAIAsync(string textoEntrada)
        {
            string apiKey = ConfigurationManager.AppSettings["OpenAIKey"];
            string endpoint = "https://api.openai.com/v1/chat/completions";



            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            HttpClientHandler handler = new HttpClientHandler
            {
                UseProxy = false // Cambiar según sea necesario
            };

            using (HttpClient httpClient = new HttpClient(handler))
            {
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                var payload = new
                {
                    model = "gpt-4o-mini",
                    messages = new[]
                    {
        new {
            role = "system",
            content = @"Eres un asistente que analiza los comentarios de clientes que los vendedores de una empresa distribuidora de productos de estética y peluquería registran en su sistema informático inmediatamente después de cada visita o llamada.
Tu tarea es ayudar a identificar:
1. **Tareas pendientes**: Busca en los comentarios acciones que el vendedor menciona como necesarias pero que aún no se han completado (por ejemplo: 'recordar', 'enviar', 'mandar muestras', 'solicitar catálogo'). Excluye aquellas acciones que indican que ya se realizaron (como 'ya ofrecí', 'ya entregué', etc.).
2. **Oportunidades de venta**: Detecta posibles ventas que podrían cerrarse con una acción comercial adicional.

Devuelve el resultado mostrando:
- Una lista de tareas pendientes (asegurándote de mencionar siempre el cliente relacionado).
- Una lista de oportunidades de venta.
- Un resumen adicional con cualquier otra información relevante para el seguimiento semanal.

Además, evalúa la calidad de los comentarios teniendo en cuenta:
- La longitud y el nivel de detalle (se valoran más los comentarios largos y detallados; los comentarios breves, de menos de 30 palabras, se consideran de menor calidad).
- La claridad en la información comercial y la mención de marcas de productos o tratamientos estéticos o de peluquería que realiza. 
- La información detallada sobre futuras oportunidades de venta 
- **La puntualidad en el registro**: este punto solo es válido para visitas presenciales, pero no para teléfono o WhatsApp (para teléfono o WhatsApp ignoramos la puntualidad en el registro completamente). Analiza las marcas de tiempo de cada comentario para determinar si se han registrado en el momento de la visita.
    - Si, en un mismo día, el primer comentario se registra al inicio de la jornada (por ejemplo, alrededor de las 9:00) y el último se registra hacia el final (por ejemplo, cerca de las 19:00) – o, al menos, si la diferencia entre el primer y el último comentario es de varias horas (por ejemplo, 4 horas o más) –, indica que el vendedor registra en tiempo real y aumenta la puntuación.
    - Si la diferencia entre el primer y el último comentario de un día es muy corta (por ejemplo, menos de 1 hora, o con un promedio inferior a 5-10 minutos entre registros), se penaliza la puntuación, ya que se asume que se ingresaron en bloque al final de la jornada.
- Representa la puntuación de calidad con de 1 a 5 iconos de estrellas (evitando usar 3 estrellas para evitar ambigüedad; utiliza 2 o 4 según corresponda). Nunca daremos más de dos estrellas si las visitas que no sean teléfono o whatapp no se registran en el momento.
- Añade una breve explicación justificando la puntuación, y explicando al vendedor cómo debería meter los comentarios para mejorar la puntuación en futuros análisis.

Nota: La jornada laboral se considera de 9:00 a 19:00.

El mensaje resultante es importante que lo devuelvas en HTML para poner en el cuerpo de un correo, con un diseño atractivo"
        },
        new {
            role = "user",
            content = textoEntrada
        }
    },
                    max_tokens = 3000,
                    temperature = 0.4
                };



                /*
                var payload = new
                {
                    model = "gpt-4o-mini",
                    messages = new[]
                    {
                        new {
                            role = "system",
                            content = @"Eres un asistente que analiza los comentarios de clientes que los vendedores de una empresa distribuidora de productos de estética y peluquería meten a su sistema informático. 
                Tu tarea es ayudar a identificar:
                1. **Tareas pendientes**: Busca en los comentarios acciones que el vendedor menciona como necesarias, pero que no indica haber completado aún (por ejemplo: ""recordar"", ""enviar"", ""mandar muestras"", ""solicitar catálogo""). Excluye acciones que claramente ya se han realizado (como ""ya ofrecí"", ""ya entregué"", etc.).
                2. **Oportunidades de venta**: Detecta posibles ventas que podrían cerrarse con una acción comercial adicional.
                Devuelve el resultado mostrando:
                - Una lista de tareas pendientes (asegurándote de mencionar siempre el cliente relacionado).
                - Una lista de oportunidades de venta.               

                Asegúrate de que las tareas y las oportunidades estén claramente separadas y organizadas para que sean fáciles de leer.

                Incluye también cualquier otra información que creas que es suficientemente importante como para aparecer en el resumen semanal. 

                Añade al final una puntuación de la calidad de los comentarios que mete este vendedor, mostrando de 1 a 5 iconos de estrellas (completas o solo contorno), tomando los siguientes criterios para valorar:
                - Valoramos poco los comentarios muy breves o repetitivos y mucho los comentarios largos con información detallada y útil o con bastantes tareas para recordarle. Un valor de referencia podría ser tomar como breve un comentario de menos de 40 palabras.
                - Sube también la puntuación si contiene información comercial detallada y algún recordatorio claro                
                - No penalices en esta puntuación el no poder contactar a los clientes, ya que no suele ser culpa del vendedor.
                - Lo más importante para la puntuación es que el comentario se registre según se acaba la visita o llamada. Si se hace al final del día o al día siguiente, baja la puntuación. Si se hace en el momento, sube la puntuación.
                - La jornada de trabajo es aproximadamente de 9h a 19h.
                - Calcula el tiempo entre el primer y el último comentario del día. Si es menos de una hora, baja la puntuación. Si es más de una hora, sube la puntuación.
                
                Si has puesto 3 estrellas, cámbialo por 2 o por 4 porque 3 queda ambiguo, no se sabe si está bien o mal. 

                Añade una breve explicación de por qué no le has dado más estrellas o como conseguir más con los comentarios de la semana que viene. 
                Si has penalizado por no hacer los comentarios en el momento, añade una explicación que detalle el tiempo medio entre los comentarios.

                El mensaje resultante es importante que lo devuelvas en HTML para poner en el cuerpo de un correo, con un diseño atractivo"
                        },
                        new {
                            role = "user",
                            content = textoEntrada
                        }
                    },
                    max_tokens = 3000,
                    temperature = 0.4 // Menor temperatura para respuestas más precisas
                };
                */



                StringContent content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                HttpResponseMessage response = await httpClient.PostAsync(endpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    string responseString = await response.Content.ReadAsStringAsync();
                    dynamic responseJson = JsonConvert.DeserializeObject<dynamic>(responseString);
                    return responseJson?.choices[0]?.message?.content?.ToString();
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Error: {response.StatusCode}");
                    Console.WriteLine($"Detalles del error: {errorContent}");
                }

                return null;
            }
        }

        /*
        // GET: api/SeguimientosClientes/5
        [ResponseType(typeof(SeguimientoCliente))]
        public async Task<IHttpActionResult> GetSeguimientoCliente(string id)
        {
            SeguimientoCliente seguimientoCliente = await db.SeguimientosClientes.FindAsync(id);
            if (seguimientoCliente == null)
            {
                return NotFound();
            }

            return Ok(seguimientoCliente);
        }
        */

        // PUT: api/SeguimientosClientes/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutSeguimientoCliente(SeguimientoClienteDTO seguimientoClienteDTO)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            SeguimientoCliente seguimientoCliente = db.SeguimientosClientes.SingleOrDefault(s => s.NºOrden == seguimientoClienteDTO.Id);

            if (seguimientoCliente.Usuario.Trim() != seguimientoClienteDTO.Usuario.Trim())
            {
                throw new Exception("No se pueden modificar los rapport de otro usuario");
            }

            seguimientoCliente.Aparatos = seguimientoClienteDTO.Aparatos;
            seguimientoCliente.Aviso = seguimientoClienteDTO.Aviso;
            seguimientoCliente.ClienteNuevo = seguimientoClienteDTO.ClienteNuevo;
            seguimientoCliente.Comentarios = seguimientoClienteDTO.Comentarios;
            seguimientoCliente.Contacto = seguimientoClienteDTO.Contacto;
            if (seguimientoCliente.Empresa?.Trim() != seguimientoClienteDTO.Empresa?.Trim())
            {
                seguimientoCliente.Empresa = seguimientoClienteDTO.Empresa;
            }
            seguimientoCliente.Estado = (short)seguimientoClienteDTO.Estado;
            seguimientoCliente.Fecha = seguimientoClienteDTO.Fecha;
            seguimientoCliente.GestiónAparatos = seguimientoClienteDTO.GestionAparatos;
            seguimientoCliente.Número = seguimientoClienteDTO.Cliente;
            seguimientoCliente.Pedido = seguimientoClienteDTO.Pedido;
            seguimientoCliente.PrimeraVisita = seguimientoClienteDTO.PrimeraVisita;
            seguimientoCliente.Tipo = seguimientoClienteDTO.Tipo;
            //seguimientoCliente.Vendedor = seguimientoClienteDTO.Vendedor;


            db.Entry(seguimientoCliente).State = EntityState.Modified;

            try
            {
                _ = await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SeguimientoClienteExists(seguimientoClienteDTO.Id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return StatusCode(HttpStatusCode.NoContent);
        }


        // POST: api/SeguimientosClientes
        [ResponseType(typeof(SeguimientoCliente))]
        public async Task<IHttpActionResult> PostSeguimientoCliente(SeguimientoClienteDTO seguimientoClienteDTO)
        {
            if (!ModelState.IsValid || seguimientoClienteDTO == null)
            {
                return BadRequest(ModelState);
            }

            DateTime fechaDesde = new DateTime(seguimientoClienteDTO.Fecha.Year, seguimientoClienteDTO.Fecha.Month, seguimientoClienteDTO.Fecha.Day);
            DateTime fechaHasta = fechaDesde.AddDays(1);
            if (seguimientoClienteDTO.Estado != SeguimientoClienteDTO.EstadoSeguimientoDTO.Gestion_Administrativa && db.SeguimientosClientes.Where(s => s.Fecha >= fechaDesde &&
                s.Fecha < fechaHasta && s.Número == seguimientoClienteDTO.Cliente &&
                s.Contacto == seguimientoClienteDTO.Contacto &&
                s.Usuario.ToLower() == seguimientoClienteDTO.Usuario.ToLower()).Any())
            {
                throw new Exception(string.Format("Ya existe un seguimiento del cliente {0}/{1} para el día {2}", seguimientoClienteDTO.Cliente.Trim(), seguimientoClienteDTO.Contacto.Trim(), fechaDesde.ToShortDateString()));
            }

            string vendedorFicha = db.Clientes.SingleOrDefault(c => c.Empresa == seguimientoClienteDTO.Empresa && c.Nº_Cliente == seguimientoClienteDTO.Cliente && c.Contacto == seguimientoClienteDTO.Contacto).Vendedor?.Trim();
            string vendedorPeluqueria = db.VendedoresClientesGruposProductos.SingleOrDefault(v => v.Empresa == seguimientoClienteDTO.Empresa && v.Cliente == seguimientoClienteDTO.Cliente && v.Contacto == seguimientoClienteDTO.Contacto && v.GrupoProducto == "PEL")?.Vendedor?.Trim();
            string vendedorUsuario = ParametrosUsuarioController.LeerParametro(seguimientoClienteDTO.Empresa, seguimientoClienteDTO.Usuario, "Vendedor");

            seguimientoClienteDTO.Vendedor = vendedorUsuario;

            SeguimientoCliente seguimientoCliente = new SeguimientoCliente
            {
                Aparatos = seguimientoClienteDTO.Aparatos,
                Aviso = seguimientoClienteDTO.Aviso,
                ClienteNuevo = seguimientoClienteDTO.ClienteNuevo,
                Comentarios = seguimientoClienteDTO.Comentarios,
                Contacto = seguimientoClienteDTO.Contacto,
                Empresa = seguimientoClienteDTO.Empresa,
                Estado = (short)seguimientoClienteDTO.Estado,
                Fecha = seguimientoClienteDTO.Fecha,
                GestiónAparatos = seguimientoClienteDTO.GestionAparatos,
                Número = seguimientoClienteDTO.Cliente,
                Pedido = seguimientoClienteDTO.Pedido,
                PrimeraVisita = seguimientoClienteDTO.PrimeraVisita,
                Tipo = seguimientoClienteDTO.Tipo,
                NumOrdenExtracto = seguimientoClienteDTO.NumOrdenExtracto,
                Usuario = seguimientoClienteDTO.Usuario
            };

            seguimientoCliente.Vendedor = seguimientoClienteDTO.Vendedor == vendedorFicha || seguimientoClienteDTO.Vendedor == vendedorPeluqueria
                ? seguimientoClienteDTO.Vendedor
                : null;

            if (seguimientoClienteDTO.TipoCentro == SeguimientoClienteDTO.TiposCentro.SoloPeluqueria && vendedorFicha != vendedorPeluqueria)
            {
                // poner vendedor general en ficha
                Cliente cliente = db.Clientes.SingleOrDefault(c => c.Empresa == seguimientoClienteDTO.Empresa && c.Nº_Cliente == seguimientoClienteDTO.Cliente && c.Contacto == seguimientoClienteDTO.Contacto);
                if (cliente != null && cliente.Vendedor != null && cliente.Vendedor.Trim() != Constantes.Vendedores.VENDEDOR_GENERAL)
                {
                    cliente.Usuario = seguimientoClienteDTO.Usuario;
                    cliente.Vendedor = Constantes.Vendedores.VENDEDOR_GENERAL;
                }
            }

            if (seguimientoClienteDTO.TipoCentro == SeguimientoClienteDTO.TiposCentro.SoloEstetica && vendedorFicha != vendedorPeluqueria)
            {
                // poner vendedor general en peluquería
                VendedorClienteGrupoProducto clienteGrupo = db.VendedoresClientesGruposProductos.SingleOrDefault(c => c.Empresa == seguimientoClienteDTO.Empresa && c.Cliente == seguimientoClienteDTO.Cliente && c.Contacto == seguimientoClienteDTO.Contacto && c.GrupoProducto == "PEL");
                if (clienteGrupo != null && clienteGrupo.Vendedor != null && clienteGrupo.Vendedor.Trim() != Constantes.Vendedores.VENDEDOR_GENERAL)
                {
                    clienteGrupo.Usuario = seguimientoCliente.Usuario;
                    clienteGrupo.Vendedor = Constantes.Vendedores.VENDEDOR_GENERAL;
                }
            }

            _ = db.SeguimientosClientes.Add(seguimientoCliente);

            try
            {
                _ = await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (SeguimientoClienteExists(seguimientoCliente.NºOrden))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }
            catch (Exception ex)
            {
                throw new Exception("No se ha podido guardar el seguimiento", ex);
            }

            return CreatedAtRoute("DefaultApi", new { id = seguimientoCliente.NºOrden }, seguimientoCliente);
        }
        /*
        // DELETE: api/SeguimientosClientes/5
        [ResponseType(typeof(SeguimientoCliente))]
        public async Task<IHttpActionResult> DeleteSeguimientoCliente(int id)
        {
            SeguimientoCliente seguimientoCliente = await db.SeguimientosClientes.SingleOrDefaultAsync(s=>s.NºOrden==id);
            if (seguimientoCliente == null)
            {
                return NotFound();
            }

            db.SeguimientosClientes.Remove(seguimientoCliente);
            await db.SaveChangesAsync();

            return Ok(seguimientoCliente);
        }
        */
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool SeguimientoClienteExists(int id)
        {
            return db.SeguimientosClientes.Count(e => e.NºOrden == id) > 0;
        }
    }
}