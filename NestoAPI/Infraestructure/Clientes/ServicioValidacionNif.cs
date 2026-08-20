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

        /// <summary>Catálogo L7: 02 = NIF-IVA (intracomunitario). Tipo por defecto de un cliente
        /// con país fiscal de la UE distinto de España.</summary>
        internal const string TIPO_NIF_IVA = "02";

        /// <summary>
        /// NestoAPI#375: tipo L7 "documento oficial de identificación expedido por el país o
        /// territorio de residencia". A diferencia del 02 (NIF-IVA), la AEAT NO lo valida contra
        /// el censo VIES: es el tipo correcto para el destinatario de una venta OSS (que por
        /// definición no está en VIES; si lo estuviera, la venta sería exenta intracomunitaria).
        /// </summary>
        internal const string TIPO_DOC_OFICIAL_PAIS = "04";

        /// <summary>NestoAPI#391: tipo L7 "no censado". Para clientes españoles cuyo NIF esté
        /// bien formado pero NO en el censo de la AEAT. OJO (fallo 20/08/26, cliente 9093): la
        /// AEAT SÍ valida que el ID del tipo 07 tenga FORMATO de NIF — no vale un relleno tipo
        /// "1000000"; sin el NIF real no hay forma de declarar la factura completa.</summary>
        internal const string TIPO_NO_CENSADO = "07";

        /// <summary>Catálogo L7: 03 = pasaporte. Con CodigoPais ES la AEAT solo admite 03 y 07
        /// (error 1233 de Verifactu si se manda otro tipo).</summary>
        internal const string TIPO_PASAPORTE = "03";
        private const string PAIS_ESPANA = "ES";

        /// <summary>
        /// Marcador que engancha una factura a la rama "formato rechazado" de la ventana de NIF
        /// incorrectos (#363): el listado busca este texto en VerifactuUltimoError (LIKE con
        /// collation acento-insensible, por eso también casa con el "no tiene un formato válido"
        /// que devuelve Verifacti). Cualquier error propio que deba salir en esa ventana (p. ej.
        /// la exclusión por NO CENSADO con NIF de relleno, fallo 20/08/26) tiene que contener
        /// este marcador — el 20/08 un mensaje que no lo llevaba dejó al cliente 9093 INVISIBLE
        /// en la ventana y sin sitio donde meter el DNI real al conseguirlo.
        /// </summary>
        internal const string MARCADOR_ERROR_FORMATO_NIF = "no tiene un formato válido";

        /// <summary>
        /// Formato SINTÁCTICO de identificación fiscal española (DNI, NIE o CIF), carácter de
        /// control incluido. No consulta el censo: es el algoritmo oficial. La AEAT lo exige en
        /// el ID del IDOtro tipo 07 (no censado): el NIF no está censado pero tiene que SER un
        /// NIF (fallo 20/08/26: el relleno "1000000" del cliente 9093 se envió como 07 y
        /// Verifacti lo rechazó con "El campo id_otro.id no tiene un formato válido").
        /// Pura y estática para testear sin BD.
        /// </summary>
        internal static bool TieneFormatoNif(string nif)
        {
            string valor = nif?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(valor) || valor.Length != 9)
            {
                return false;
            }

            const string LETRAS_DNI = "TRWAGMYFPDXBNJZSQVHLCKE";
            // DNI: 8 dígitos + letra de control (resto de dividir entre 23)
            if (valor.Substring(0, 8).All(char.IsDigit))
            {
                return valor[8] == LETRAS_DNI[int.Parse(valor.Substring(0, 8)) % 23];
            }
            // NIE: X/Y/Z + 7 dígitos + letra (misma tabla, con X=0, Y=1, Z=2 delante)
            if ("XYZ".IndexOf(valor[0]) >= 0 && valor.Substring(1, 7).All(char.IsDigit))
            {
                int numero = int.Parse("XYZ".IndexOf(valor[0]) + valor.Substring(1, 7));
                return valor[8] == LETRAS_DNI[numero % 23];
            }
            // CIF: letra de organización + 7 dígitos + control (dígito o letra según entidad;
            // se aceptan ambos, que es lo que valida la AEAT sintácticamente)
            if ("ABCDEFGHJKLMNPQRSUVW".IndexOf(valor[0]) >= 0 && valor.Substring(1, 7).All(char.IsDigit))
            {
                int suma = 0;
                for (int i = 1; i <= 7; i++)
                {
                    int digito = valor[i] - '0';
                    if (i % 2 == 1) // posiciones impares (1ª, 3ª...): se doblan y suman sus cifras
                    {
                        int doble = digito * 2;
                        suma += (doble / 10) + (doble % 10);
                    }
                    else
                    {
                        suma += digito;
                    }
                }
                int control = (10 - (suma % 10)) % 10;
                return valor[8] == (char)('0' + control) || valor[8] == "JABCDEFGHI"[control];
            }
            return false;
        }

        /// <summary>NestoAPI#354: estados ISO-2 de la UE (mirror de Paises.UnionEuropea, que no
        /// está en el EDMX). Un cliente con país fiscal aquí y distinto de ES se declara a
        /// Verifactu con IDOtro tipo 02 (NIF-IVA) sin pasar por el censo español. La pertenencia
        /// a la UE es estable; si cambia (adhesiones), actualizar aquí y en la tabla Paises.</summary>
        internal static readonly HashSet<string> PaisesUnionEuropea = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "GR", "HU",
            "IE", "IT", "LV", "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "SE"
        };

        /// <summary>NestoAPI#354: país fiscal de la UE distinto de España (destinatario B2B
        /// intracomunitario → IDOtro tipo 02). ES y no-UE (que necesitan tipo específico marcado
        /// a mano: pasaporte, doc. del país...) quedan fuera.</summary>
        internal static bool EsPaisUnionEuropeaDistintoDeEspana(string pais)
        {
            string codigo = pais?.Trim();
            return !string.IsNullOrEmpty(codigo)
                && !codigo.Equals(PAIS_ESPANA, StringComparison.OrdinalIgnoreCase)
                && PaisesUnionEuropea.Contains(codigo);
        }

        // Criterio único en Constantes.ClientesEspeciales (#325/#366): la lista de clientes
        // ficticios de simplificadas ya no vive duplicada aquí (DRY, ajuste 17/08/26 al añadir
        // MATERIALES_CURSOS).
        private static bool EsClienteSimplificadas(string cliente)
            => Constantes.ClientesEspeciales.EsClienteFacturaSimplificada(cliente);

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
            if (EsClienteSimplificadas(cliente))
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
            string nombrePrincipal = principal.Nombre?.Trim();
            foreach (CabFacturaVta factura in facturasSinDeclarar)
            {
                bool corregida = false;
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
                    corregida = true;
                }
                // NestoAPI#383: al censo viaja el PAR NIF/NOMBRE persistido en la factura; si solo
                // se corrige el NIF y el nombre cambió (apellido por matrimonio), sigue atascada.
                // El nombre del principal ya está validado contra la AEAT unas líneas más arriba.
                if (!string.IsNullOrWhiteSpace(nombrePrincipal) && factura.NombreFiscal?.Trim() != nombrePrincipal)
                {
                    _ = db.Modificaciones.Add(new Modificacion
                    {
                        Tabla = "CabFacturaVta",
                        Anterior = $"Factura {factura.Número?.Trim()} NombreFiscal={factura.NombreFiscal?.Trim()}",
                        Nuevo = $"NombreFiscal={nombrePrincipal} (corrección centralizada #383, factura sin declarar)",
                        Usuario = usuario
                    });
                    factura.NombreFiscal = nombrePrincipal;
                    corregida = true;
                }
                // Fallo 20/08/26: la factura pudo quedar EXCLUIDA del job ("SinDatosFiscales",
                // p. ej. NO CENSADO con NIF de relleno) o con un rechazo previo persistido.
                // Corregir el NIF debe REABRIRLA (VerifactuEstado null = el job la reintenta),
                // igual que ya hacía MarcarIdentificacionExtranjera (#348) — si no, las facturas
                // de 9093 seguirían excluidas para siempre aun con el DNI bueno. También si el
                // NIF ya estaba bien: una factura SIN declarar con estado informado es siempre
                // una exclusión o un rechazo, nunca una declaración en curso (esas tienen UUID).
                if (factura.VerifactuEstado != null)
                {
                    factura.VerifactuEstado = null;
                    corregida = true;
                }
                if (corregida)
                {
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

        /// <summary>
        /// NestoAPI#383 (caso real NV2612562/940): factura atascada en Verifactu por el par
        /// NIF/NOMBRE cuando el NIF es bueno pero el nombre persistido ya no casa con el censo
        /// (apellido cambiado por matrimonio). Se prueban candidatos censales por orden de
        /// fiabilidad y se adopta el nombre que la AEAT confirme (en NO IDENTIFICADO-SIMILAR
        /// devuelve el censal exacto). Best-effort: con la AEAT caída no se toca nada (el job
        /// vuelve a intentarlo en la siguiente pasada).
        /// </summary>
        public async Task<bool> CorregirNombreFiscalFactura(CabFacturaVta factura, string usuario)
        {
            string nif = factura?.CifNif?.Trim();
            if (string.IsNullOrEmpty(nif))
            {
                return false;
            }
            string nombreActual = factura.NombreFiscal?.Trim();

            foreach (string candidato in await BuscarNombresCensalesCandidatos(factura, nif, nombreActual).ConfigureAwait(false))
            {
                RespuestaNifNombreCliente respuesta;
                try
                {
                    respuesta = await servicioAeat.ComprobarNifNombre(NifParaCenso(nif), candidato).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception(
                        $"ValidacionNif: VNifV2 no disponible al buscar el nombre censal de {nif} " +
                        $"(factura {factura.Número?.Trim()}): {ex.Message}", ex));
                    return false;
                }
                string resultado = respuesta?.ResultadoAeat?.Trim().ToUpper();
                // BAJA/REVOCADO validan el NIF pero el nombre viene con prefijo de aviso: no vale.
                if (resultado != "IDENTIFICADO" && resultado != "NO IDENTIFICADO-SIMILAR")
                {
                    continue;
                }
                string nombreCensal = string.IsNullOrWhiteSpace(respuesta.NombreFormateado)
                    ? candidato
                    : respuesta.NombreFormateado.Trim();
                if (nombreCensal == nombreActual)
                {
                    continue; // ya estaba así: el rechazo será por otra causa
                }
                _ = db.Modificaciones.Add(new Modificacion
                {
                    Tabla = "CabFacturaVta",
                    Anterior = $"Factura {factura.Número?.Trim()} NombreFiscal={nombreActual}",
                    Nuevo = $"NombreFiscal={nombreCensal} (nombre censal #383, AEAT: {respuesta.ResultadoAeat})",
                    Usuario = usuario
                });
                factura.NombreFiscal = nombreCensal;
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
                return true;
            }
            return false;
        }

        /// <summary>Candidatos a nombre censal para el NIF de la factura, por orden de
        /// fiabilidad: la ficha principal de su cliente (renombrada tras el rechazo), el nombre
        /// de otra factura del mismo NIF YA ACEPTADA por la AEAT y el de otra ficha con ese NIF
        /// (cliente nuevo creado con los datos buenos). Todos se verifican contra la AEAT antes
        /// de usarse: esto solo decide qué probar, no qué escribir.</summary>
        private async Task<List<string>> BuscarNombresCensalesCandidatos(CabFacturaVta factura, string nif, string nombreActual)
        {
            var candidatos = new List<string>();

            Cliente principal = await db.Clientes.FirstOrDefaultAsync(c =>
                c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                && c.Nº_Cliente == factura.Nº_Cliente && c.ClientePrincipal).ConfigureAwait(false);
            if (principal != null)
            {
                candidatos.Add(principal.Nombre?.Trim());
            }

            List<string> deFacturasAceptadas = await db.CabsFacturasVtas
                .Where(f => f.CifNif == nif && f.VerifactuUUID != null && f.VerifactuUUID != ""
                    && f.VerifactuEstado == "Correcto" && f.NombreFiscal != null)
                .OrderByDescending(f => f.Fecha)
                .Select(f => f.NombreFiscal)
                .Take(3)
                .ToListAsync().ConfigureAwait(false);
            candidatos.AddRange(deFacturasAceptadas.Select(n => n?.Trim()));

            List<string> deOtrasFichas = await db.Clientes
                .Where(c => c.CIF_NIF == nif && c.ClientePrincipal && c.Nº_Cliente != factura.Nº_Cliente)
                .Select(c => c.Nombre)
                .Take(3)
                .ToListAsync().ConfigureAwait(false);
            candidatos.AddRange(deOtrasFichas.Select(n => n?.Trim()));

            return candidatos
                .Where(n => !string.IsNullOrWhiteSpace(n) && n != nombreActual)
                .Distinct()
                .ToList();
        }

        public async Task<int> UnificarNifContactos(string cliente, string usuario)
        {
            if (EsClienteSimplificadas(cliente))
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

            // NestoAPI#391: los clientes de facturas simplificadas (F2, sin destinatario) no
            // llevan NIF real. Las demás rutas ya los saltan (MarcarIncorrecto, CalcularEstado),
            // pero una validación INCORRECTO antigua los dejaba clavados en la ventana (caso
            // 31794) sin nada que corregir. Se excluyen de ambas ramas.
            string condicionSimplificadas = ConstruirCondicionClientesSimplificadas(parametros);

            string sql =
                "SELECT LTRIM(RTRIM(v.Cliente)) AS Cliente, LTRIM(RTRIM(v.Contacto)) AS Contacto, " +
                "       v.Nombre, v.Nif, v.ResultadoAeat, v.FechaValidacion, LTRIM(RTRIM(c.Vendedor)) AS Vendedor, " +
                "       CAST(CASE WHEN EXISTS (SELECT 1 FROM LinPedidoVta l " +
                "               WHERE l.Empresa = c.Empresa AND l.[Nº Cliente] = c.[Nº Cliente] " +
                "               AND l.Estado >= -1 AND l.Estado <= 2) THEN 1 ELSE 0 END AS bit) AS TienePedidoPendiente, " +
                // NestoAPI#354: la sugerencia se calcula en C# tras materializar; la columna NULL
                // existe solo para que SqlQuery pueda mapear el DTO.
                "       CAST(NULL AS varchar(2)) AS PaisIntracomunitarioSugerido " +
                "FROM ValidacionesNif v " +
                "INNER JOIN Clientes c ON c.Empresa = v.Empresa AND c.[Nº Cliente] = v.Cliente AND c.Contacto = v.Contacto " +
                // 17/08/26 (caso 31794 MATERIALES CURSOS): una ficha ANULADA (Estado negativo)
                // no debe salir en la ventana — ya no se le puede facturar, no hay nada que
                // corregir. Aplica a ambas ramas del UNION.
                "WHERE v.Estado = @p0 AND c.[CIF/NIF] = v.Nif AND c.Nombre = v.Nombre " +
                "  AND c.Estado >= 0 " +
                condicionSimplificadas +
                condicionVendedor +
                // NestoAPI#363: además de los INCORRECTO del censo AEAT, se listan los clientes cuyo
                // envío a Verifactu falló por FORMATO de IVA/NIF (típicamente extranjeros con el VAT
                // truncado a char(9) o mal escrito). Así aparecen en la MISMA ventana y se corrigen
                // desde Nesto con "Marcar como extranjero" (VAT completo). El NOT EXISTS evita duplicar
                // a los que ya salen como INCORRECTO.
                "UNION " +
                "SELECT LTRIM(RTRIM(c.[Nº Cliente])), LTRIM(RTRIM(c.Contacto)), c.Nombre, c.[CIF/NIF], " +
                "       'VERIFACTU: formato de IVA/NIF rechazado', " +
                "       ISNULL(MAX(f.VerifactuUltimoIntento), CAST('20000101' AS datetime)), LTRIM(RTRIM(c.Vendedor)), " +
                "       CAST(CASE WHEN EXISTS (SELECT 1 FROM LinPedidoVta l " +
                "               WHERE l.Empresa = c.Empresa AND l.[Nº Cliente] = c.[Nº Cliente] " +
                "               AND l.Estado >= -1 AND l.Estado <= 2) THEN 1 ELSE 0 END AS bit), " +
                "       CAST(NULL AS varchar(2)) " +
                "FROM CabFacturaVta f " +
                "INNER JOIN Clientes c ON c.Empresa = f.Empresa AND c.[Nº Cliente] = f.[Nº Cliente] AND c.Contacto = f.Contacto " +
                // El marcador compartido (collation AI: casa con y sin acento) engancha tanto los
                // rechazos de Verifacti como las exclusiones propias (NO CENSADO con relleno).
                $"WHERE f.VerifactuUltimoError LIKE '%{MARCADOR_ERROR_FORMATO_NIF}%' " +
                "  AND c.Estado >= 0 " +
                condicionSimplificadas +
                "  AND NOT EXISTS (SELECT 1 FROM ValidacionesNif v2 WHERE v2.Empresa = c.Empresa " +
                "        AND v2.Cliente = c.[Nº Cliente] AND v2.Contacto = c.Contacto AND v2.Estado = @p0 " +
                "        AND c.[CIF/NIF] = v2.Nif AND c.Nombre = v2.Nombre) " +
                condicionVendedor +
                "GROUP BY c.Empresa, c.[Nº Cliente], c.Contacto, c.Nombre, c.[CIF/NIF], c.Vendedor " +
                "ORDER BY TienePedidoPendiente DESC, FechaValidacion DESC";

            List<ClienteNifIncorrectoDTO> lista = await db.Database.SqlQuery<ClienteNifIncorrectoDTO>(sql, parametros.ToArray())
                .ToListAsync().ConfigureAwait(false);
            // NestoAPI#354: si el NIF parece un NIF-IVA intracomunitario (prefijo de país UE),
            // se sugiere el país para que la pantalla de Nesto#417 ofrezca "marcar como
            // extranjero tipo 02" con un clic. Solo sugerencia: la decisión sigue siendo humana.
            foreach (ClienteNifIncorrectoDTO fila in lista)
            {
                fila.PaisIntracomunitarioSugerido = DetectarPaisNifIvaIntracomunitario(fila.Nif);
            }
            return lista;
        }

        // NestoAPI#391: fragmento SQL "NOT IN" con los clientes de facturas simplificadas,
        // añadiendo sus parámetros a la lista. Internal para poder testearlo.
        internal static string ConstruirCondicionClientesSimplificadas(List<object> parametros)
        {
            List<string> clientes = Constantes.ClientesEspeciales.ClientesFacturaSimplificada.ToList();
            if (!clientes.Any())
            {
                return string.Empty;
            }
            var marcadores = new List<string>();
            for (int i = 0; i < clientes.Count; i++)
            {
                marcadores.Add($"@s{i}");
                parametros.Add(new SqlParameter($"@s{i}", clientes[i]));
            }
            return $"AND c.[Nº Cliente] NOT IN ({string.Join(", ", marcadores)}) ";
        }

        // Prefijos de NIF-IVA intracomunitario (EU-27 sin ES + XI Irlanda del Norte). Grecia usa
        // EL en el VAT pero GR como país; XI se declara con GB.
        private static readonly Dictionary<string, string> _prefijosVatUe = new Dictionary<string, string>
        {
            ["AT"] = "AT", ["BE"] = "BE", ["BG"] = "BG", ["CY"] = "CY", ["CZ"] = "CZ",
            ["DE"] = "DE", ["DK"] = "DK", ["EE"] = "EE", ["EL"] = "GR", ["FI"] = "FI",
            ["FR"] = "FR", ["HR"] = "HR", ["HU"] = "HU", ["IE"] = "IE", ["IT"] = "IT",
            ["LT"] = "LT", ["LU"] = "LU", ["LV"] = "LV", ["MT"] = "MT", ["NL"] = "NL",
            ["PL"] = "PL", ["PT"] = "PT", ["RO"] = "RO", ["SE"] = "SE", ["SI"] = "SI",
            ["SK"] = "SK", ["XI"] = "GB"
        };

        /// <summary>
        /// NestoAPI#354: país (ISO-2) si el NIF tiene pinta de NIF-IVA intracomunitario (dos letras
        /// de país UE distinto de ES + al menos un dígito después), o null. Los NIE (X/Y/Z + dígitos)
        /// y los CIF españoles (una letra) no casan porque su segundo carácter es numérico.
        /// </summary>
        internal static string DetectarPaisNifIvaIntracomunitario(string nif)
        {
            string limpio = nif?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(limpio) || limpio.Length < 5)
            {
                return null;
            }
            string prefijo = limpio.Substring(0, 2);
            if (!_prefijosVatUe.TryGetValue(prefijo, out string pais))
            {
                return null;
            }
            string resto = limpio.Substring(2);
            return resto.Any(char.IsDigit) ? pais : null;
        }

        public async Task MarcarIncorrecto(string cliente, string motivo, string usuario)
        {
            if (EsClienteSimplificadas(cliente))
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

            if (EsClienteSimplificadas(ficha.Nº_Cliente))
            {
                resultado.Estado = EstadoValidacionNif.Excluido;
                return resultado;
            }

            ValidacionNifRegistro registro = await almacen
                .Leer(ficha.Empresa?.Trim(), ficha.Nº_Cliente?.Trim(), ficha.Contacto?.Trim())
                .ConfigureAwait(false);
            bool marcaVigente = registro != null && registro.Nif?.Trim() == nif && registro.Nombre?.Trim() == nombre;

            // 1. NestoAPI#339: marca extranjera EXPLÍCITA vigente (el usuario eligió tipo y país;
            // puede ser no-UE, pasaporte...): manda sobre todo lo demás.
            if (marcaVigente && registro.Estado == ESTADO_EXTRANJERO)
            {
                resultado.Estado = EstadoValidacionNif.Extranjero;
                resultado.TipoIdentificacion = registro.TipoIdentificacion?.Trim();
                resultado.Pais = registro.Pais?.Trim();
                resultado.ResultadoAeat = registro.ResultadoAeat;
                return resultado;
            }

            // 2. NestoAPI#354: país fiscal de la UE distinto de ES → NIF-IVA intracomunitario
            // (IDOtro tipo 02) AUTOMÁTICO, sin pasar por el censo español (un VAT extranjero
            // jamás valida ahí). Clientes.Pais es la fuente de verdad: así un cliente dado de
            // alta con país IT/FR/DE se declara bien a Verifactu sin el rodeo por NIF incorrectos.
            if (EsPaisUnionEuropeaDistintoDeEspana(ficha.Pais))
            {
                resultado.Estado = EstadoValidacionNif.Extranjero;
                resultado.TipoIdentificacion = TIPO_NIF_IVA;
                resultado.Pais = ficha.Pais?.Trim().ToUpperInvariant();
                resultado.ResultadoAeat = $"IDOtro tipo {TIPO_NIF_IVA} ({resultado.Pais}) por país fiscal";
                return resultado;
            }

            // 3. Veredicto de censo cacheado (NIF español). Sin registro vigente → sin validar.
            if (!marcaVigente)
            {
                resultado.Estado = EstadoValidacionNif.SinValidar;
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
            if (EsClienteSimplificadas(cliente))
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

            // Fallo 20/08/26 (cliente 9093 de Amparo): la AEAT exige que el ID del tipo 07 (no
            // censado) tenga FORMATO de NIF — con el relleno "1000000" la marca se guardaba y
            // luego CADA reintento del job fallaba con "El campo id_otro.id no tiene un formato
            // válido". Se valida AQUÍ, al marcar, con el motivo explicado: sin el NIF real (bien
            // formado, aunque no esté censado) no hay forma legal de declarar la factura completa.
            // Además, con país ES la AEAT solo admite los tipos 03 (pasaporte) y 07 (error 1233).
            if (tipoIdentificacion == TIPO_NO_CENSADO)
            {
                if (pais != PAIS_ESPANA)
                {
                    return new ResultadoCorreccionNif
                    {
                        Corregido = false,
                        Motivo = "El tipo 07 (no censado) es solo para NIF españoles: el país debe ser ES."
                    };
                }
                string nifPrevisto = !string.IsNullOrWhiteSpace(nifNuevo) ? nifNuevo : principal.CIF_NIF?.Trim();
                if (!TieneFormatoNif(nifPrevisto))
                {
                    return new ResultadoCorreccionNif
                    {
                        Corregido = false,
                        Motivo = $"No se puede marcar como NO CENSADO: la AEAT exige que el identificador sea un " +
                            $"NIF con formato válido (aunque no esté censado) y '{nifPrevisto}' no lo es. " +
                            "Hay que conseguir el NIF real del cliente y corregirlo primero."
                    };
                }
            }
            else if (pais == PAIS_ESPANA && tipoIdentificacion != TIPO_PASAPORTE)
            {
                return new ResultadoCorreccionNif
                {
                    Corregido = false,
                    Motivo = $"Con país ES la AEAT solo admite los tipos 03 (pasaporte) y 07 (no censado); " +
                        $"el tipo {tipoIdentificacion} se rechazaría (error 1233 de Verifactu)."
                };
            }

            // NestoAPI#356/#354: si se indica el NIF-IVA extranjero COMPLETO se propaga a las fichas
            // y a las facturas sin declarar. El char(9) antiguo lo truncaba (IT+11 dígitos → 9), y
            // "Marcar extranjero" solo copiaba ese valor mutilado, así que Verifacti seguía
            // rechazándolo ("El IVA no tiene formato válido"). NO se valida contra la AEAT: un
            // NIF-IVA intracomunitario no está en el censo español.
            // NestoAPI#354: marcar como extranjero fija también el PAÍS FISCAL (Clientes.Pais) en
            // todas las fichas del cliente, para que sea coherente con la marca y (si es de la UE)
            // el país por sí solo baste para declarar con IDOtro en el futuro.
            foreach (Cliente ficha in fichas)
            {
                if (ficha.Pais?.Trim().ToUpperInvariant() != pais)
                {
                    ficha.Pais = pais;
                }
            }

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

            // NestoAPI#391: el tipo 07 (no censado) se usa también para clientes ESPAÑOLES cuyo
            // NIF real no se puede conseguir; el mensaje no debe hablar de "extranjera".
            bool esNoCensado = tipoIdentificacion == TIPO_NO_CENSADO;
            _ = db.Modificaciones.Add(new Modificacion
            {
                Tabla = "Clientes",
                Anterior = $"Cliente {principal.Nº_Cliente?.Trim()} identificación {principal.CIF_NIF?.Trim()}",
                Nuevo = esNoCensado
                    ? $"Marcada como NO CENSADO (IDOtro 07, país {pais}) (#391)"
                    : $"Marcada como EXTRANJERA tipo {tipoIdentificacion} país {pais} (#339)",
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
                Motivo = (esNoCensado
                    ? $"Cliente marcado como NO CENSADO (IDOtro tipo 07, país {pais}): "
                    : $"Identificación marcada como extranjera (tipo {tipoIdentificacion}, país {pais}): ") +
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
