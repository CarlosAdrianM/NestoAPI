using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#327: valida el NIF de las fichas contra el censo de la AEAT (VNifV2) y cachea
    /// el veredicto en ValidacionesNif. El estado efectivo compara la ficha ACTUAL con lo que
    /// se validó: si el NIF o el nombre cambiaron después, vuelve a "sin validar" solo.
    /// </summary>
    public class ServicioValidacionNif : IServicioValidacionNif
    {
        public const string ESTADO_CORRECTO = "CORRECTO";
        public const string ESTADO_INCORRECTO = "INCORRECTO";
        /// <summary>NestoAPI#339: identificación extranjera marcada a mano (pasaporte...).</summary>
        public const string ESTADO_EXTRANJERO = "EXTRANJERO";

        /// <summary>Catálogo L7 de la AEAT para IDOtro: 02 NIF-IVA, 03 pasaporte, 04 documento
        /// oficial del país de residencia, 05 certificado de residencia, 06 otro documento
        /// probatorio, 07 no censado.</summary>
        internal static readonly HashSet<string> TiposIdentificacionValidos = new HashSet<string>
        {
            "02", "03", "04", "05", "06", "07"
        };

        private static readonly HashSet<string> ClientesSimplificadas = new HashSet<string>
        {
            Constantes.ClientesEspeciales.AMAZON,
            Constantes.ClientesEspeciales.TIENDA_ONLINE,
            Constantes.ClientesEspeciales.PUBLICO_FINAL
        };

        private readonly NVEntities db;
        private readonly IAlmacenValidacionesNif almacen;
        private readonly IServicioGestorClientes servicioAeat;

        public ServicioValidacionNif(NVEntities db, IAlmacenValidacionesNif almacen = null,
            IServicioGestorClientes servicioAeat = null)
        {
            this.db = db;
            this.almacen = almacen ?? new AlmacenValidacionesNif(db);
            this.servicioAeat = servicioAeat ?? new ServicioGestorClientes();
        }

        public async Task<ResultadoValidacionNif> ObtenerEstado(string empresa, string cliente, string contacto)
        {
            Cliente ficha = await LeerFicha(empresa, cliente, contacto).ConfigureAwait(false);
            if (ficha == null)
            {
                return new ResultadoValidacionNif { Estado = EstadoValidacionNif.SinValidar };
            }
            return await CalcularEstado(ficha).ConfigureAwait(false);
        }

        public async Task<ResultadoValidacionNif> ValidarSiHaceFalta(string empresa, string cliente, string contacto, string usuario)
        {
            Cliente ficha = await LeerFicha(empresa, cliente, contacto).ConfigureAwait(false);
            if (ficha == null)
            {
                return new ResultadoValidacionNif { Estado = EstadoValidacionNif.SinValidar };
            }

            ResultadoValidacionNif estadoActual = await CalcularEstado(ficha).ConfigureAwait(false);
            if (estadoActual.Estado != EstadoValidacionNif.SinValidar)
            {
                return estadoActual; // cacheado (o excluido): no se vuelve a preguntar a la AEAT
            }

            string nif = ficha.CIF_NIF?.Trim();
            string nombre = ficha.Nombre?.Trim();
            if (string.IsNullOrWhiteSpace(nif))
            {
                // Sin NIF no hay nada que validar contra el censo: se queda sin validar
                // (la factura F1 fallará por otra validación; las simplificadas están excluidas).
                return estadoActual;
            }

            // NestoAPI#339: un identificador extranjero (NIF-IVA intracomunitario "IT012...",
            // etc.) NUNCA validará contra el censo español — sin esta guarda daría falso
            // INCORRECTO con correo al vendedor. Se queda sin validar hasta que #339 defina
            // el tratamiento (IDOtro de Verifactu). Los pasaportes no se distinguen aún.
            if (EsIdentificadorExtranjero(nif))
            {
                return estadoActual;
            }

            RespuestaNifNombreCliente respuesta;
            try
            {
                respuesta = await servicioAeat.ComprobarNifNombre(NifParaCenso(nif), nombre).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // La AEAT no responde: no se bloquea nada ni se cachea nada; se reintentará
                // en el siguiente pedido/factura. Best-effort con traza.
                ElmahHelper.Log(new Exception(
                    $"ValidacionNif: VNifV2 no disponible al validar {nif} del cliente {cliente?.Trim()}/{contacto?.Trim()}: {ex.Message}", ex));
                return estadoActual;
            }

            var registro = new ValidacionNifRegistro
            {
                Empresa = empresa?.Trim(),
                Cliente = cliente?.Trim(),
                Contacto = contacto?.Trim(),
                Nif = nif,
                Nombre = nombre,
                Estado = respuesta.NifValidado ? ESTADO_CORRECTO : ESTADO_INCORRECTO,
                ResultadoAeat = respuesta.ResultadoAeat,
                FechaValidacion = DateTime.Now,
                Usuario = usuario
            };
            await almacen.Guardar(registro).ConfigureAwait(false);

            return new ResultadoValidacionNif
            {
                Estado = respuesta.NifValidado ? EstadoValidacionNif.Correcto : EstadoValidacionNif.Incorrecto,
                Nif = nif,
                Nombre = nombre,
                ResultadoAeat = respuesta.ResultadoAeat,
                AcabaDeResultarIncorrecto = !respuesta.NifValidado
            };
        }

        public async Task<ResultadoValidacionNif> ValidarPrincipal(string cliente, string usuario)
        {
            Cliente principal = await db.Clientes.FirstOrDefaultAsync(c =>
                c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                && c.Nº_Cliente == cliente && c.ClientePrincipal)
                .ConfigureAwait(false);
            if (principal == null)
            {
                // Integridad de ClientePrincipal aparte (#331): sin principal no se valida nada.
                return new ResultadoValidacionNif { Estado = EstadoValidacionNif.SinValidar };
            }
            return await ValidarSiHaceFalta(principal.Empresa, principal.Nº_Cliente, principal.Contacto, usuario)
                .ConfigureAwait(false);
        }

        public async Task<ResultadoCorreccionNif> CorregirNif(string cliente, string nifNuevo, string usuario)
        {
            nifNuevo = nifNuevo?.Trim().ToUpper();
            if (string.IsNullOrWhiteSpace(nifNuevo))
            {
                return new ResultadoCorreccionNif { Corregido = false, Motivo = "El NIF no puede estar vacío." };
            }
            if (ClientesSimplificadas.Contains(cliente?.Trim()))
            {
                return new ResultadoCorreccionNif { Corregido = false, Motivo = "Los clientes de facturas simplificadas no llevan NIF real." };
            }

            List<Cliente> fichas = await db.Clientes
                .Where(c => c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && c.Nº_Cliente == cliente)
                .ToListAsync().ConfigureAwait(false);
            Cliente principal = fichas.FirstOrDefault(c => c.ClientePrincipal) ?? fichas.FirstOrDefault();
            if (principal == null)
            {
                return new ResultadoCorreccionNif { Corregido = false, Motivo = $"No existe el cliente {cliente?.Trim()}." };
            }

            // Validar contra la AEAT ANTES de tocar nada: si Hacienda lo rechaza, la ficha se
            // queda como está (no vamos a sustituir un NIF malo por otro).
            RespuestaNifNombreCliente respuesta = await servicioAeat
                .ComprobarNifNombre(nifNuevo, principal.Nombre?.Trim()).ConfigureAwait(false);
            if (!respuesta.NifValidado)
            {
                return new ResultadoCorreccionNif
                {
                    Corregido = false,
                    Nif = nifNuevo,
                    ResultadoAeat = respuesta.ResultadoAeat,
                    Motivo = $"La AEAT no reconoce el NIF {nifNuevo} para '{principal.Nombre?.Trim()}' " +
                        $"({respuesta.ResultadoAeat ?? "NO IDENTIFICADO"}). No se ha modificado nada."
                };
            }

            // Propagar a TODOS los contactos (#330: todos los contactos comparten NIF) y
            // auditar: estamos modificando un dato fiscal.
            int actualizados = 0;
            foreach (Cliente ficha in fichas)
            {
                if (ficha.CIF_NIF?.Trim() != nifNuevo)
                {
                    _ = db.Modificaciones.Add(new Modificacion
                    {
                        Tabla = "Clientes",
                        Anterior = $"Cliente {ficha.Nº_Cliente?.Trim()}/{ficha.Contacto?.Trim()} CIF_NIF={ficha.CIF_NIF?.Trim()}",
                        Nuevo = $"CIF_NIF={nifNuevo} (corrección centralizada #327, AEAT: {respuesta.ResultadoAeat})",
                        Usuario = usuario
                    });
                    ficha.CIF_NIF = nifNuevo;
                    actualizados++;
                }
            }

            // Carlos 22/07: las facturas ya EMITIDAS y aún sin declarar a Verifactu llevan el
            // NIF viejo PERSISTIDO (a la AEAT viaja factura.CifNif, no la ficha): sin esto, el
            // reintento del job las mandaría mal para siempre. Solo dentro de la ventana de
            // declaración de la sombra (el histórico pre-Verifactu tiene UUID null legítimo).
            System.DateTime fechaInicioDeclaracion = Verifactu.VerifactuJobsService.FechaInicioDeclaracion;
            List<CabFacturaVta> facturasSinDeclarar = await db.CabsFacturasVtas
                .Where(f => f.Nº_Cliente == cliente && f.Fecha >= fechaInicioDeclaracion
                    && (f.VerifactuUUID == null || f.VerifactuUUID == ""))
                .ToListAsync().ConfigureAwait(false);
            int facturasActualizadas = 0;
            foreach (CabFacturaVta factura in facturasSinDeclarar)
            {
                if (factura.CifNif?.Trim() != nifNuevo)
                {
                    _ = db.Modificaciones.Add(new Modificacion
                    {
                        Tabla = "CabFacturaVta",
                        Anterior = $"Factura {factura.Número?.Trim()} CifNif={factura.CifNif?.Trim()}",
                        Nuevo = $"CifNif={nifNuevo} (corrección centralizada #327, factura sin declarar)",
                        Usuario = usuario
                    });
                    factura.CifNif = nifNuevo;
                    facturasActualizadas++;
                }
            }
            _ = await db.SaveChangesAsync().ConfigureAwait(false);

            // Registrar la validación del principal (es el NIF que se declara al facturar).
            await almacen.Guardar(new ValidacionNifRegistro
            {
                Empresa = principal.Empresa?.Trim(),
                Cliente = principal.Nº_Cliente?.Trim(),
                Contacto = principal.Contacto?.Trim(),
                Nif = nifNuevo,
                Nombre = principal.Nombre?.Trim(),
                Estado = ESTADO_CORRECTO,
                ResultadoAeat = respuesta.ResultadoAeat,
                FechaValidacion = DateTime.Now,
                Usuario = usuario
            }).ConfigureAwait(false);

            return new ResultadoCorreccionNif
            {
                Corregido = true,
                Nif = nifNuevo,
                ResultadoAeat = respuesta.ResultadoAeat,
                NombreAeat = respuesta.NombreFormateado,
                ContactosActualizados = actualizados,
                FacturasActualizadas = facturasActualizadas
            };
        }

        /// <summary>
        /// NestoAPI#339: los NIF-IVA intracomunitarios empiezan por el código de país (dos
        /// letras: IT, FR, PT...). Ningún formato español empieza por dos letras (DNI: dígito;
        /// NIE: X/Y/Z + dígitos; CIF: UNA letra + dígitos), así que dos letras iniciales =
        /// identificador extranjero — SALVO "ES", que es el prefijo del NIF-IVA ESPAÑOL
        /// (matiz de Carlos 21/07): ese sí se valida contra el censo (sin el prefijo).
        /// Pasaportes y otros documentos quedan para #339.
        /// </summary>
        internal static bool EsIdentificadorExtranjero(string nif)
        {
            return nif != null && nif.Length >= 2 && char.IsLetter(nif[0]) && char.IsLetter(nif[1])
                && !nif.StartsWith("ES", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>El censo de la AEAT valida el NIF pelado: a un NIF-IVA español ("ES" +
        /// NIF) hay que quitarle el prefijo antes de preguntar (la ficha se queda tal cual).</summary>
        internal static string NifParaCenso(string nif)
        {
            return nif != null && nif.Length > 2 && nif.StartsWith("ES", StringComparison.OrdinalIgnoreCase)
                ? nif.Substring(2)
                : nif;
        }

        public async Task<int> UnificarNifContactos(string cliente, string usuario)
        {
            if (ClientesSimplificadas.Contains(cliente?.Trim()))
            {
                return 0;
            }

            List<Cliente> fichas = await db.Clientes
                .Where(c => c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && c.Nº_Cliente == cliente)
                .ToListAsync().ConfigureAwait(false);
            Cliente principal = fichas.FirstOrDefault(c => c.ClientePrincipal);
            string nifPrincipal = principal?.CIF_NIF?.Trim();
            if (principal == null || string.IsNullOrWhiteSpace(nifPrincipal))
            {
                return 0; // sin principal (integridad #331) o sin NIF: nada que propagar
            }

            // Regla de #330: SOLO se propaga un NIF con veredicto CORRECTO de la AEAT.
            ResultadoValidacionNif estadoPrincipal = await CalcularEstado(principal).ConfigureAwait(false);
            if (estadoPrincipal.Estado != EstadoValidacionNif.Correcto)
            {
                return 0;
            }

            int corregidos = 0;
            foreach (Cliente ficha in fichas.Where(f => !f.ClientePrincipal && f.CIF_NIF?.Trim() != nifPrincipal))
            {
                // Auditar: se modifica un dato fiscal sin intervención humana (#330).
                _ = db.Modificaciones.Add(new Modificacion
                {
                    Tabla = "Clientes",
                    Anterior = $"Cliente {ficha.Nº_Cliente?.Trim()}/{ficha.Contacto?.Trim()} CIF_NIF={ficha.CIF_NIF?.Trim()}",
                    Nuevo = $"CIF_NIF={nifPrincipal} (propagado del principal validado contra la AEAT, #330)",
                    Usuario = usuario
                });
                ficha.CIF_NIF = nifPrincipal;
                corregidos++;
            }
            if (corregidos > 0)
            {
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
            }
            return corregidos;
        }

        public async Task<List<ClienteNifIncorrectoDTO>> ListarNifIncorrectos(List<string> vendedores = null)
        {
            // Solo validaciones VIGENTES: si la ficha cambió de NIF/nombre después de validar,
            // el join no casa y la ficha no sale (está "sin validar", no "incorrecta").
            // Pedido pendiente de servir o facturar = líneas en estado PENDIENTE..ALBARAN.
            // Nesto#417: el filtro admite VARIOS vendedores (jefe de equipo = él + su equipo).
            List<string> filtro = vendedores?.Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim()).Distinct().ToList() ?? new List<string>();
            var parametros = new List<object> { new SqlParameter("@p0", ESTADO_INCORRECTO) };
            string condicionVendedor = string.Empty;
            if (filtro.Any())
            {
                var marcadores = new List<string>();
                for (int i = 0; i < filtro.Count; i++)
                {
                    marcadores.Add($"@v{i}");
                    parametros.Add(new SqlParameter($"@v{i}", filtro[i]));
                }
                condicionVendedor = $"AND c.Vendedor IN ({string.Join(", ", marcadores)}) ";
            }

            string sql =
                "SELECT LTRIM(RTRIM(v.Cliente)) AS Cliente, LTRIM(RTRIM(v.Contacto)) AS Contacto, " +
                "       v.Nombre, v.Nif, v.ResultadoAeat, v.FechaValidacion, LTRIM(RTRIM(c.Vendedor)) AS Vendedor, " +
                "       CAST(CASE WHEN EXISTS (SELECT 1 FROM LinPedidoVta l " +
                "               WHERE l.Empresa = c.Empresa AND l.[Nº Cliente] = c.[Nº Cliente] " +
                "               AND l.Estado >= -1 AND l.Estado <= 2) THEN 1 ELSE 0 END AS bit) AS TienePedidoPendiente " +
                "FROM ValidacionesNif v " +
                "INNER JOIN Clientes c ON c.Empresa = v.Empresa AND c.[Nº Cliente] = v.Cliente AND c.Contacto = v.Contacto " +
                "WHERE v.Estado = @p0 AND c.[CIF/NIF] = v.Nif AND c.Nombre = v.Nombre " +
                condicionVendedor +
                "ORDER BY TienePedidoPendiente DESC, v.FechaValidacion DESC";

            return await db.Database.SqlQuery<ClienteNifIncorrectoDTO>(sql, parametros.ToArray())
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task MarcarIncorrecto(string cliente, string motivo, string usuario)
        {
            if (ClientesSimplificadas.Contains(cliente?.Trim()))
            {
                return;
            }
            Cliente principal = await db.Clientes.FirstOrDefaultAsync(c =>
                c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                && c.Nº_Cliente == cliente && c.ClientePrincipal)
                .ConfigureAwait(false);
            if (principal == null || string.IsNullOrWhiteSpace(principal.CIF_NIF))
            {
                return;
            }
            await almacen.Guardar(new ValidacionNifRegistro
            {
                Empresa = principal.Empresa?.Trim(),
                Cliente = principal.Nº_Cliente?.Trim(),
                Contacto = principal.Contacto?.Trim(),
                Nif = principal.CIF_NIF?.Trim(),
                Nombre = principal.Nombre?.Trim(),
                Estado = ESTADO_INCORRECTO,
                ResultadoAeat = motivo?.Length > 100 ? motivo.Substring(0, 100) : motivo,
                FechaValidacion = DateTime.Now,
                Usuario = usuario
            }).ConfigureAwait(false);
        }

        private async Task<Cliente> LeerFicha(string empresa, string cliente, string contacto)
        {
            return await db.Clientes.FirstOrDefaultAsync(c =>
                c.Empresa == empresa && c.Nº_Cliente == cliente && c.Contacto == contacto)
                .ConfigureAwait(false);
        }

        private async Task<ResultadoValidacionNif> CalcularEstado(Cliente ficha)
        {
            string nif = ficha.CIF_NIF?.Trim();
            string nombre = ficha.Nombre?.Trim();
            var resultado = new ResultadoValidacionNif { Nif = nif, Nombre = nombre };

            if (ClientesSimplificadas.Contains(ficha.Nº_Cliente?.Trim()))
            {
                resultado.Estado = EstadoValidacionNif.Excluido;
                return resultado;
            }

            ValidacionNifRegistro registro = await almacen
                .Leer(ficha.Empresa?.Trim(), ficha.Nº_Cliente?.Trim(), ficha.Contacto?.Trim())
                .ConfigureAwait(false);

            // Sin registro, o la ficha cambió de NIF/nombre después de validar → sin validar.
            if (registro == null || registro.Nif?.Trim() != nif || registro.Nombre?.Trim() != nombre)
            {
                resultado.Estado = EstadoValidacionNif.SinValidar;
                return resultado;
            }

            // NestoAPI#339: identificación extranjera vigente (mientras la ficha no cambie de
            // NIF/nombre): no valida contra el censo y a Verifactu va como IDOtro.
            if (registro.Estado == ESTADO_EXTRANJERO)
            {
                resultado.Estado = EstadoValidacionNif.Extranjero;
                resultado.TipoIdentificacion = registro.TipoIdentificacion?.Trim();
                resultado.Pais = registro.Pais?.Trim();
                resultado.ResultadoAeat = registro.ResultadoAeat;
                return resultado;
            }

            resultado.Estado = registro.Estado == ESTADO_CORRECTO
                ? EstadoValidacionNif.Correcto
                : EstadoValidacionNif.Incorrecto;
            resultado.ResultadoAeat = registro.ResultadoAeat;
            return resultado;
        }

        // NestoAPI#339: pasaportes y demás identificaciones extranjeras. La marca vive en
        // ValidacionesNif (misma caducidad natural: si la ficha cambia de NIF/nombre, vuelve
        // a "sin validar" y habría que marcarla de nuevo).
        public async Task<ResultadoCorreccionNif> MarcarIdentificacionExtranjera(string cliente,
            string tipoIdentificacion, string pais, string usuario, string nifNuevo = null)
        {
            tipoIdentificacion = tipoIdentificacion?.Trim();
            pais = pais?.Trim().ToUpper();
            nifNuevo = nifNuevo?.Trim().ToUpper();
            if (!TiposIdentificacionValidos.Contains(tipoIdentificacion))
            {
                return new ResultadoCorreccionNif
                {
                    Corregido = false,
                    Motivo = "Tipo de identificación no válido. Use 02 (NIF-IVA), 03 (pasaporte), " +
                        "04 (documento del país), 05 (certificado de residencia), 06 (otro) o 07 (no censado)."
                };
            }
            if (string.IsNullOrWhiteSpace(pais) || pais.Length != 2 || !pais.All(char.IsLetter))
            {
                return new ResultadoCorreccionNif { Corregido = false, Motivo = "Hay que indicar el país en formato ISO de 2 letras (FR, MA, GB...)." };
            }
            if (ClientesSimplificadas.Contains(cliente?.Trim()))
            {
                return new ResultadoCorreccionNif { Corregido = false, Motivo = "Los clientes de facturas simplificadas no llevan identificación real." };
            }

            List<Cliente> fichas = await db.Clientes
                .Where(c => c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && c.Nº_Cliente == cliente)
                .ToListAsync().ConfigureAwait(false);
            Cliente principal = fichas.FirstOrDefault(c => c.ClientePrincipal) ?? fichas.FirstOrDefault();
            if (principal == null || (string.IsNullOrWhiteSpace(principal.CIF_NIF) && string.IsNullOrWhiteSpace(nifNuevo)))
            {
                return new ResultadoCorreccionNif { Corregido = false, Motivo = $"No existe el cliente {cliente?.Trim()} o su ficha no tiene identificación." };
            }

            // NestoAPI#356/#354: si se indica el NIF-IVA extranjero COMPLETO se propaga a las fichas
            // y a las facturas sin declarar. El char(9) antiguo lo truncaba (IT+11 dígitos → 9), y
            // "Marcar extranjero" solo copiaba ese valor mutilado, así que Verifacti seguía
            // rechazándolo ("El IVA no tiene formato válido"). NO se valida contra la AEAT: un
            // NIF-IVA intracomunitario no está en el censo español.
            int fichasActualizadas = 0;
            int facturasActualizadas = 0;
            if (!string.IsNullOrWhiteSpace(nifNuevo))
            {
                foreach (Cliente ficha in fichas)
                {
                    if (ficha.CIF_NIF?.Trim() != nifNuevo)
                    {
                        _ = db.Modificaciones.Add(new Modificacion
                        {
                            Tabla = "Clientes",
                            Anterior = $"Cliente {ficha.Nº_Cliente?.Trim()}/{ficha.Contacto?.Trim()} CIF_NIF={ficha.CIF_NIF?.Trim()}",
                            Nuevo = $"CIF_NIF={nifNuevo} (NIF-IVA extranjero completo #356)",
                            Usuario = usuario
                        });
                        ficha.CIF_NIF = nifNuevo;
                        fichasActualizadas++;
                    }
                }

                // Las facturas ya emitidas y sin declarar llevan el NIF truncado persistido (a la
                // AEAT viaja factura.CifNif): sin esto, el reintento del job las mandaría mal para
                // siempre. Solo dentro de la ventana de declaración de la sombra.
                System.DateTime fechaInicioDeclaracion = Verifactu.VerifactuJobsService.FechaInicioDeclaracion;
                List<CabFacturaVta> facturasSinDeclarar = await db.CabsFacturasVtas
                    .Where(f => f.Nº_Cliente == cliente && f.Fecha >= fechaInicioDeclaracion
                        && (f.VerifactuUUID == null || f.VerifactuUUID == ""))
                    .ToListAsync().ConfigureAwait(false);
                foreach (CabFacturaVta factura in facturasSinDeclarar)
                {
                    if (factura.CifNif?.Trim() != nifNuevo)
                    {
                        _ = db.Modificaciones.Add(new Modificacion
                        {
                            Tabla = "CabFacturaVta",
                            Anterior = $"Factura {factura.Número?.Trim()} CifNif={factura.CifNif?.Trim()}",
                            Nuevo = $"CifNif={nifNuevo} (NIF-IVA extranjero completo #356, factura sin declarar)",
                            Usuario = usuario
                        });
                        factura.CifNif = nifNuevo;
                        // NestoAPI#348: si la factura se había excluido por "sin datos fiscales" o
                        // por un rechazo previo, se reabre para que el job la reintente ya corregida.
                        factura.VerifactuEstado = null;
                        facturasActualizadas++;
                    }
                }
            }

            string nifFinal = !string.IsNullOrWhiteSpace(nifNuevo) ? nifNuevo : principal.CIF_NIF?.Trim();

            await almacen.Guardar(new ValidacionNifRegistro
            {
                Empresa = principal.Empresa?.Trim(),
                Cliente = principal.Nº_Cliente?.Trim(),
                Contacto = principal.Contacto?.Trim(),
                Nif = nifFinal,
                Nombre = principal.Nombre?.Trim(),
                Estado = ESTADO_EXTRANJERO,
                ResultadoAeat = $"IDOtro tipo {tipoIdentificacion} ({pais})",
                FechaValidacion = DateTime.Now,
                Usuario = usuario,
                TipoIdentificacion = tipoIdentificacion,
                Pais = pais
            }).ConfigureAwait(false);

            _ = db.Modificaciones.Add(new Modificacion
            {
                Tabla = "Clientes",
                Anterior = $"Cliente {principal.Nº_Cliente?.Trim()} identificación {principal.CIF_NIF?.Trim()}",
                Nuevo = $"Marcada como EXTRANJERA tipo {tipoIdentificacion} país {pais} (#339)",
                Usuario = usuario
            });
            _ = await db.SaveChangesAsync().ConfigureAwait(false);

            string extra = fichasActualizadas > 0 || facturasActualizadas > 0
                ? $" NIF actualizado a {nifFinal} en {fichasActualizadas} ficha(s) y {facturasActualizadas} factura(s) sin declarar."
                : string.Empty;
            return new ResultadoCorreccionNif
            {
                Corregido = true,
                Nif = nifFinal,
                ContactosActualizados = fichasActualizadas,
                FacturasActualizadas = facturasActualizadas,
                Motivo = $"Identificación marcada como extranjera (tipo {tipoIdentificacion}, país {pais}): " +
                    "deja de validarse contra el censo y las facturas se declararán con IDOtro." + extra
            };
        }
    }

    /// <summary>
    /// Acceso por SQL crudo a ValidacionesNif (tabla fuera del EDMX, patrón Cargos/EstadosCCC).
    /// </summary>
    public class AlmacenValidacionesNif : IAlmacenValidacionesNif
    {
        private readonly NVEntities db;

        public AlmacenValidacionesNif(NVEntities db)
        {
            this.db = db;
        }

        public async Task<ValidacionNifRegistro> Leer(string empresa, string cliente, string contacto)
        {
            List<ValidacionNifRegistro> filas = await db.Database.SqlQuery<ValidacionNifRegistro>(
                "SELECT Empresa, Cliente, Contacto, Nif, Nombre, Estado, ResultadoAeat, FechaValidacion, Usuario, " +
                "TipoIdentificacion, Pais " +
                "FROM ValidacionesNif WHERE Empresa = @p0 AND Cliente = @p1 AND Contacto = @p2",
                empresa, cliente, contacto).ToListAsync().ConfigureAwait(false);
            ValidacionNifRegistro registro = filas.FirstOrDefault();
            if (registro != null)
            {
                registro.Empresa = registro.Empresa?.Trim();
                registro.Cliente = registro.Cliente?.Trim();
                registro.Contacto = registro.Contacto?.Trim();
                registro.Nif = registro.Nif?.Trim();
                registro.Nombre = registro.Nombre?.Trim();
                registro.Estado = registro.Estado?.Trim();
                registro.TipoIdentificacion = registro.TipoIdentificacion?.Trim();
                registro.Pais = registro.Pais?.Trim();
            }
            return registro;
        }

        public async Task Guardar(ValidacionNifRegistro registro)
        {
            _ = await db.Database.ExecuteSqlCommandAsync(
                "UPDATE ValidacionesNif SET Nif = @p3, Nombre = @p4, Estado = @p5, ResultadoAeat = @p6, " +
                "FechaValidacion = GETDATE(), Usuario = @p7, TipoIdentificacion = @p8, Pais = @p9 " +
                "WHERE Empresa = @p0 AND Cliente = @p1 AND Contacto = @p2; " +
                "IF @@ROWCOUNT = 0 " +
                "INSERT INTO ValidacionesNif (Empresa, Cliente, Contacto, Nif, Nombre, Estado, ResultadoAeat, Usuario, TipoIdentificacion, Pais) " +
                "VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9)",
                new SqlParameter("@p0", registro.Empresa),
                new SqlParameter("@p1", registro.Cliente),
                new SqlParameter("@p2", registro.Contacto),
                new SqlParameter("@p3", registro.Nif),
                new SqlParameter("@p4", registro.Nombre),
                new SqlParameter("@p5", registro.Estado),
                new SqlParameter("@p6", (object)registro.ResultadoAeat ?? DBNull.Value),
                new SqlParameter("@p7", (object)registro.Usuario ?? DBNull.Value),
                new SqlParameter("@p8", (object)registro.TipoIdentificacion ?? DBNull.Value),
                new SqlParameter("@p9", (object)registro.Pais ?? DBNull.Value))
                .ConfigureAwait(false);
        }
    }
}
