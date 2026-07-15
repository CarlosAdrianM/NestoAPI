using NestoAPI.Models;
using NestoAPI.Models.ApuntesBanco;
using NestoAPI.Models.Bancos;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Contabilidad
{
    public class ContabilidadService : IContabilidadService
    {
        private readonly IRepositorioTerminalesTPV _repositorioTerminales;
        private Dictionary<string, string> _mapaTerminalesResuelto;

        public ContabilidadService() : this(new RepositorioTerminalesTPV())
        {
        }

        public ContabilidadService(IRepositorioTerminalesTPV repositorioTerminales)
        {
            _repositorioTerminales = repositorioTerminales;
        }

        public async Task<int> ContabilizarDiario(string empresa, string diario, string usuario)
        {
            using (NVEntities db = new NVEntities())
            {
                return await ContabilizarDiario(db, empresa, diario, usuario);
            }
        }

        // Issue #284: para validar las FormaPago de las líneas de cliente antes de contabilizar.
        public async Task<HashSet<string>> LeerFormasPago(string empresa)
        {
            using (NVEntities db = new NVEntities())
            {
                List<string> formas = await db.FormasPago
                    .Where(f => f.Empresa == empresa)
                    .Select(f => f.Número)
                    .ToListAsync()
                    .ConfigureAwait(false);
                return new HashSet<string>(formas.Select(f => f.Trim()), StringComparer.OrdinalIgnoreCase);
            }
        }

        public async Task<int> ContabilizarDiario(NVEntities db, string empresa, string diario, string usuario)
        {
            // #296: prdContabilizar llama a prdLiquidar cuando la línea trae Liquidado; sus
            // validaciones de negocio abortan con RAISERROR enterrado en ruido de transacciones.
            // Las adelantamos aquí con un mensaje claro y accionable, ANTES de tocar el SP.
            List<PreContabilidad> lineasQueLiquidan = await db.PreContabilidades
                .Where(p => p.Empresa == empresa && p.Diario == diario && p.Liquidado != null && p.Liquidado != 0)
                .ToListAsync()
                .ConfigureAwait(false);
            if (lineasQueLiquidan.Count > 0)
            {
                List<int> destinosIds = lineasQueLiquidan.Select(l => l.Liquidado.Value).Distinct().ToList();
                Dictionary<int, ExtractoCliente> destinos = await db.ExtractosCliente
                    .Where(e => destinosIds.Contains(e.Nº_Orden))
                    .ToDictionaryAsync(e => e.Nº_Orden)
                    .ConfigureAwait(false);
                List<string> erroresLiquidacion = ErroresLiquidacionesDiario(lineasQueLiquidan,
                    id => destinos.TryGetValue(id, out ExtractoCliente destino) ? destino : null);
                if (erroresLiquidacion.Count > 0)
                {
                    throw new Exception(string.Join(" ", erroresLiquidacion));
                }
            }

            SqlParameter empresaParametro = new SqlParameter("@Empresa", SqlDbType.Char, 3)
            {
                Value = empresa
            };

            SqlParameter diarioParametro = new SqlParameter("@Diario", SqlDbType.Char, 10)
            {
                Value = diario
            };
            SqlParameter usuarioParametro = new SqlParameter("@Usuario", SqlDbType.Char, 30)
            {
                Value = usuario
            };
            //var resultadoProcedimiento = await db.Database.ExecuteSqlCommandAsync("EXEC prdContabilizar @Empresa, @Diario", empresaParametro, diarioParametro);
            //return resultadoProcedimiento;
            SqlParameter resultadoParametro = new SqlParameter
            {
                ParameterName = "@Resultado",
                SqlDbType = SqlDbType.Int,
                Direction = ParameterDirection.Output // Configurar para capturar el valor de retorno
            };
            try
            {
                // Ejecutar el procedimiento almacenado y capturar el valor de retorno
                int resultadoDirecto = await db.Database.ExecuteSqlCommandAsync("EXEC @Resultado = prdContabilizar @Empresa, @Diario, @Usuario", resultadoParametro, empresaParametro, diarioParametro, usuarioParametro);
                // Obtener el valor de retorno del parámetro
                int resultadoProcedimiento = (int)resultadoParametro.Value;
                return resultadoProcedimiento;
            }
            catch (System.Data.SqlClient.SqlException ex)
            {
                // #296: prdContabilizar/prdLiquidar abortan con RAISERROR de negocio ("Importes
                // con mismo signo o importe 0..."), pero el SqlException llega mezclado con el
                // ruido del desajuste de transacciones (error 266 repetido) que entierra la
                // causa real. Filtramos el ruido para que el usuario vea el motivo de verdad.
                throw new Exception(ComponerMensajeSinRuidoDeTransacciones(
                    ex.Errors.Cast<System.Data.SqlClient.SqlError>().Select(e => new KeyValuePair<int, string>(e.Number, e.Message))), ex);
            }
            catch (Exception ex)
            {
                throw new Exception("Error al contabilizar el diario", ex);
            }
        }

        // #296: validaciones de negocio de prdLiquidar replicadas en C# ANTES de ejecutar el SP
        // (patrón #284): destino existente, mismo cliente y signos opuestos con importes != 0.
        // El SP las revalida igualmente; esto solo adelanta un error CLARO en vez del RAISERROR
        // enterrado en ruido de transacciones.
        internal static List<string> ErroresLiquidacionesDiario(
            IEnumerable<PreContabilidad> lineasDiario,
            Func<int, ExtractoCliente> buscarExtracto)
        {
            List<string> errores = new List<string>();
            foreach (PreContabilidad linea in lineasDiario.Where(l => (l.Liquidado ?? 0) != 0))
            {
                int destinoId = linea.Liquidado.Value;
                string cliente = linea.Nº_Cuenta?.Trim();
                decimal importeLinea = linea.Debe - linea.Haber;
                ExtractoCliente destino = buscarExtracto(destinoId);
                if (destino == null)
                {
                    errores.Add($"La línea del cliente {cliente} liquida contra el movimiento {destinoId} del extracto, que no existe.");
                    continue;
                }
                if (!string.Equals(destino.Número?.Trim(), cliente, StringComparison.OrdinalIgnoreCase))
                {
                    errores.Add($"La línea del cliente {cliente} liquida contra el movimiento {destinoId}, que es del cliente {destino.Número?.Trim()}: deben ser del mismo cliente.");
                    continue;
                }
                if (importeLinea == 0 || destino.ImportePdte == 0 || Math.Sign(importeLinea) == Math.Sign(destino.ImportePdte))
                {
                    errores.Add($"No se puede liquidar el movimiento {destinoId} del cliente {cliente}: los importes deben tener signo contrario y ser distintos de 0 (línea {importeLinea:C}, pendiente del movimiento {destino.ImportePdte:C}).");
                }
            }
            return errores;
        }

        // 266 = recuento de BEGIN/COMMIT no coincidente; 3902/3903 = COMMIT/ROLLBACK sin BEGIN.
        // Son síntomas del aborto interno del SP, no la causa; la causa son los RAISERROR.
        internal static string ComponerMensajeSinRuidoDeTransacciones(IEnumerable<KeyValuePair<int, string>> errores)
        {
            List<string> negocio = errores
                .Where(e => e.Key != 266 && e.Key != 3902 && e.Key != 3903)
                .Select(e => e.Value?.Trim())
                .Where(m => !string.IsNullOrEmpty(m))
                .Distinct()
                .ToList();
            return negocio.Any()
                ? "Error al contabilizar el diario: " + string.Join(" ", negocio)
                : "Error al contabilizar el diario";
        }

        public async Task<int> CrearLineas(NVEntities db, List<PreContabilidad> lineas)
        {
            foreach (PreContabilidad linea in lineas)
            {
                if (linea.FechaVto == null || linea.FechaVto == DateTime.MinValue)
                {
                    linea.FechaVto = new DateTime(linea.Fecha.Year, linea.Fecha.Month, linea.Fecha.Day);
                }
                linea.Fecha_Modificación = DateTime.Now;
                if (linea.Concepto != null && linea.Concepto.Length > 50)
                {
                    linea.Concepto = linea.Concepto.Substring(0, 50);
                }
                _ = db.PreContabilidades.Add(linea);

                // Ejecutar prdCopiarCliente si corresponde
                if (linea.Empresa != Constantes.Empresas.EMPRESA_POR_DEFECTO &&
                    linea.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CLIENTE)
                {
                    SqlParameter empresaOrigenParam = new SqlParameter("@EmpresaOrigen", SqlDbType.Char, 3)
                    {
                        Value = Constantes.Empresas.EMPRESA_POR_DEFECTO
                    };

                    SqlParameter empresaDestinoParam = new SqlParameter("@EmpresaDestino", SqlDbType.Char, 3)
                    {
                        Value = linea.Empresa
                    };

                    SqlParameter numClienteParam = new SqlParameter("@NumCliente", SqlDbType.Char, 10)
                    {
                        Value = linea.Nº_Cuenta
                    };

                    _ = await db.Database.ExecuteSqlCommandAsync(
                        "EXEC prdCopiarCliente @EmpresaOrigen, @EmpresaDestino, @NumCliente",
                        empresaOrigenParam, empresaDestinoParam, numClienteParam);
                }
            }
            try
            {
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
                return lineas.Last().Nº_Orden;
            }
            catch (Exception ex)
            {
                throw new Exception("No se ha podido contabilizar el diario", ex);
            }

        }

        public async Task<int> CrearLineas(List<PreContabilidad> lineas)
        {
            return await ReintentarSiDeadlock(async () =>
            {
                using (NVEntities db = new NVEntities())
                {
                    return await CrearLineas(db, lineas);
                }
            }).ConfigureAwait(false);
        }

        // Issue #273: los apuntes concurrentes (Cajas/TPV/Bancos) deadlockean con la contabilización
        // (víctima 1205, ~10 casos entre el 29/06 y el 09/07). Un deadlock es transitorio por
        // definición: SQL Server revierte ENTERA la transacción víctima y la otra continúa, así que
        // reintentar la operación completa con contexto nuevo es seguro (prdCopiarCliente, que se
        // ejecuta fuera de la transacción del SaveChanges, es idempotente). NO se aplica al overload
        // que recibe el NVEntities: ahí la transacción es del llamante y el reintento le corresponde.
        // #288 punto 2: la implementación vive ahora en la policy Polly compartida ReintentosSql
        // (segundo caso: GestorClientes.ObtenerClientes); aquí se delega para conservar la firma.
        internal static Task<T> ReintentarSiDeadlock<T>(Func<Task<T>> operacion, int maxIntentos = 3, int retrasoBaseMs = 200)
        {
            return ReintentosSql.ReintentarSiDeadlockAsync(operacion, maxIntentos, retrasoBaseMs);
        }

        // ¿Hay un SqlException 1205 (elegido como víctima de interbloqueo) en la cadena?
        internal static bool EsVictimaDeDeadlock(Exception ex)
        {
            return ReintentosSql.EsVictimaDeDeadlock(ex);
        }

        public async Task<int> CrearLineasYContabilizarDiario(List<PreContabilidad> lineas)
        {
            // Issue #273: reintento ante deadlock de la operación COMPLETA (transacción nueva en cada
            // intento; la víctima se revierte entera, no queda nada a medias).
            return await ReintentarSiDeadlock(() => CrearLineasYContabilizarDiarioUnaVez(lineas)).ConfigureAwait(false);
        }

        private async Task<int> CrearLineasYContabilizarDiarioUnaVez(List<PreContabilidad> lineas)
        {
            using (NVEntities db = new NVEntities())
            {
                using (DbContextTransaction transaction = db.Database.BeginTransaction())
                {
                    try
                    {
                        int resultado = await CrearLineasYContabilizarDiario(lineas, db);
                        if (resultado > 0)
                        {
                            transaction.Commit();
                        }
                        else
                        {
                            // #291: con la transacción zombi (SP abortado por dentro) el Rollback
                            // normal lanzaría y convertiría el resultado de negocio en excepción.
                            transaction.RollbackSeguro();
                        }
                        return resultado;
                    }
                    catch (Exception ex)
                    {
                        // Issue #47 + #291: rollback seguro (la conexión puede estar cerrada o el
                        // SP haber revertido ya) para que viaje SIEMPRE la excepción original.
                        transaction.RollbackSeguro();
                        throw new Exception("Error al contabilizar el diario. Inténtelo de nuevo.", ex);
                    }
                }
            }
        }

        public async Task<int> CrearLineasYContabilizarDiario(List<PreContabilidad> lineas, NVEntities db)
        {
            // Ojo, aquí hay que entrar ya en transacción y hacer try/catch con el rollback y el commit desde el método que llama            
            _ = await CrearLineas(db, lineas);
            int resultado = await ContabilizarDiario(db, lineas[0].Empresa, lineas[0].Diario, lineas[0].Usuario);
            return resultado;
        }

        public async Task<bool> PersistirCuaderno43(ContenidoCuaderno43 contenido)
        {
            using (NVEntities db = new NVEntities())
            {
                try
                {
                    FicheroCuaderno43 fichero = new FicheroCuaderno43
                    {
                        // Si en algún momento tenemos dos cuentas en el mismo fichero, lo trataríamos como dos ficheros
                        // Asignar valores de la clase RegistroCabeceraCuenta a la instancia de FicheroCuaderno43
                        ClaveEntidad = contenido.Cabecera.ClaveEntidad,
                        ClaveOficina = contenido.Cabecera.ClaveOficina,
                        NumeroCuenta = contenido.Cabecera.NumeroCuenta,
                        FechaInicial = contenido.Cabecera.FechaInicial,
                        FechaFinal = contenido.Cabecera.FechaFinal,
                        ClaveDebeOHaber = contenido.Cabecera.ClaveDebeOHaber,
                        ImporteSaldoInicial = contenido.Cabecera.ImporteSaldoInicial,
                        ClaveDivisa = contenido.Cabecera.ClaveDivisa,
                        ModalidadInformacion = contenido.Cabecera.ModalidadInformacion,
                        NombreAbreviado = contenido.Cabecera.NombreAbreviado,

                        // Asignar valores de la clase RegistroFinalCuenta a la instancia de FicheroCuaderno43
                        // Esto está mal en fichero son re
                        TotalImportesDebe = contenido.FinalCuenta.TotalImportesDebe,
                        NumeroApuntesHaber = contenido.FinalCuenta.NumeroApuntesHaber,
                        TotalImportesHaber = contenido.FinalCuenta.TotalImportesHaber,
                        CodigoSaldoFinal = contenido.FinalCuenta.CodigoSaldoFinal,
                        SaldoFinal = contenido.FinalCuenta.SaldoFinal,

                        // Asignar valores adicionales o predeterminados
                        NumeroRegistros = contenido.FinalFichero.NumeroRegistros,
                        Usuario = contenido.Usuario,
                        FechaCreacion = DateTime.Now // Fecha actual                    
                    };

                    Banco banco = await db.Bancos.SingleAsync(b => b.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && b.Entidad == fichero.ClaveEntidad && b.Sucursal == fichero.ClaveOficina && b.Nº_Cuenta == fichero.NumeroCuenta);
                    DateTime fechaFichero = contenido.Apuntes.First().FechaOperacion;
                    if (db.ApuntesBancarios.Any(c => c.FechaOperacion == fechaFichero && c.Empresa == banco.Empresa && c.BancoId == banco.Número))
                    {
                        throw new Exception($"Ya se ha contabilizado el fichero del día {fechaFichero.ToShortDateString()}");
                    }
                    // Iterar sobre cada ApunteBancarioDTO en la lista contenido.Apuntes
                    foreach (ApunteBancarioDTO apunteDto in contenido.Apuntes)
                    {
                        // Crear una nueva instancia de ApunteBancario
                        ApunteBancario apunteBancario = new ApunteBancario
                        {
                            // Asignar valores de ApunteBancarioDTO a ApunteBancario
                            Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                            BancoId = banco.Número,
                            ClaveOficinaOrigen = apunteDto.ClaveOficinaOrigen,
                            FechaOperacion = apunteDto.FechaOperacion,
                            FechaValor = apunteDto.FechaValor,
                            ConceptoComun = apunteDto.ConceptoComun,
                            ConceptoPropio = apunteDto.ConceptoPropio,
                            ClaveDebeOHaberMovimiento = apunteDto.ClaveDebeOHaberMovimiento,
                            ImporteMovimiento = apunteDto.ImporteMovimiento,
                            NumeroDocumento = apunteDto.NumeroDocumento,
                            Referencia1 = apunteDto.Referencia1,
                            Referencia2 = apunteDto.Referencia2,
                            Usuario = contenido.Usuario,
                            FechaCreacion = DateTime.Now
                        };

                        // Iterar sobre cada ConceptoComplementario en apunteDto.RegistrosConcepto
                        foreach (ConceptoComplementario concepto in apunteDto.RegistrosConcepto)
                        {
                            // Crear una nueva instancia de RegistroComplementarioConcepto
                            RegistroComplementarioConcepto registroConcepto = new RegistroComplementarioConcepto
                            {
                                // Asignar valores de ConceptoComplementario a RegistroComplementarioConcepto
                                CodigoDato = concepto.CodigoDatoConcepto,
                                Concepto = concepto.Concepto,
                                Concepto2 = concepto.Concepto2,
                                Usuario = contenido.Usuario,
                                FechaCreacion = DateTime.Now
                            };


                            // Agregar RegistroComplementarioConcepto a la colección de apunteBancario
                            apunteBancario.RegistroComplementarioConceptoes.Add(registroConcepto);
                        }

                        // Si hay un objeto ImporteEquivalencia en apunteDto
                        if (apunteDto.ImporteEquivalencia != null && apunteDto.ImporteEquivalencia.ImporteEquivalencia != 0)
                        {
                            // Crear una nueva instancia de RegistroComplementarioEquivalencia
                            RegistroComplementarioEquivalencia registroEquivalencia = new RegistroComplementarioEquivalencia
                            {
                                // Asignar valores de EquivalenciaDivisas a RegistroComplementarioEquivalencia
                                CodigoDato = apunteDto.ImporteEquivalencia.CodigoDatoEquivalencia,
                                ClaveDivisaOrigen = apunteDto.ImporteEquivalencia.ClaveDivisaOrigen,
                                ImporteEquivalencia = apunteDto.ImporteEquivalencia.ImporteEquivalencia,
                                Usuario = contenido.Usuario,
                                FechaCreacion = DateTime.Now
                            };

                            // Agregar RegistroComplementarioEquivalencia a la colección de apunteBancario
                            apunteBancario.RegistroComplementarioEquivalencias.Add(registroEquivalencia);
                        }

                        // Agregar apunteBancario a la colección de apuntes bancarios de fichero
                        fichero.ApuntesBancarios.Add(apunteBancario);
                    }

                    // Agregar la instancia de FicheroCuaderno43 al contexto y guardar los cambios
                    _ = db.FicherosCuaderno43.Add(fichero);

                    _ = await db.SaveChangesAsync();
                    return true;
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
        }

        public async Task<bool> PersistirMovimientosTPV(List<MovimientoTPVDTO> movimientosTPV)
        {
            using (NVEntities db = new NVEntities())
            {
                DateTime fechaFichero = movimientosTPV.First().FechaOperacion;
                if (db.MovimientosTPV.Any(c => c.FechaCaptura == fechaFichero))
                {
                    throw new Exception($"Ya se ha contabilizado el fichero de tarjetas del día {fechaFichero.ToShortDateString()}");
                }

                try
                {
                    foreach (MovimientoTPVDTO movimiento in movimientosTPV)
                    {
                        MovimientoTPV nuevoMovimiento = new MovimientoTPV
                        {
                            ModoCaptura = movimiento.ModoCaptura,
                            Sesion = movimiento.Sesion,
                            Terminal = movimiento.Terminal,
                            FechaCaptura = movimiento.FechaCaptura,
                            FechaOperacion = movimiento.FechaOperacion,
                            ImporteOperacion = movimiento.ImporteOperacion,
                            ImporteComision = movimiento.ImporteComision,
                            ImporteAbono = movimiento.ImporteAbono,
                            CodigoMoneda = movimiento.CodigoMoneda,
                            Comentarios = movimiento.Comentarios,
                            Usuario = movimiento.Usuario,
                            FechaCreacion = DateTime.Now
                        };
                        _ = db.MovimientosTPV.Add(nuevoMovimiento);
                    }
                    _ = await db.SaveChangesAsync();
                    return true;
                }
                catch (Exception ex)
                {
                    throw new Exception("No se ha podido persistir el fichero de tarjetas", ex);
                }

            }
        }

        public async Task<decimal> SaldoFinal(string entidad, string oficina, string cuenta, DateTime fecha)
        {
            using (NVEntities db = new NVEntities())
            {
                FicheroCuaderno43 contenido = await db.FicherosCuaderno43
                    .Where(c => c.ClaveEntidad == entidad && c.ClaveOficina == oficina && c.NumeroCuenta == cuenta && c.FechaFinal <= fecha)
                    .OrderByDescending(c => c.FechaFinal)
                    .FirstOrDefaultAsync();

                return contenido != null ? contenido.SaldoFinal : 0m;
            }
        }

        public async Task<decimal> SaldoInicial(string entidad, string oficina, string cuenta, DateTime fecha)
        {
            using (NVEntities db = new NVEntities())
            {
                FicheroCuaderno43 contenido = await db.FicherosCuaderno43
                    .Where(c =>
                        c.ClaveEntidad == entidad &&
                        c.ClaveOficina == oficina &&
                        c.NumeroCuenta == cuenta &&
                        c.FechaInicial <= fecha &&
                        c.FechaFinal >= fecha)
                    .OrderByDescending(c => c.FechaInicial) // por si hubiera solapamiento, cogemos el más reciente
                    .FirstOrDefaultAsync()
                    .ConfigureAwait(false);

                return contenido?.ImporteSaldoInicial ?? 0m;
            }
        }


        public async Task<List<MovimientoTPVDTO>> LeerMovimientosTPV(DateTime fechaCaptura, string tipoDatafono)
        {
            using (NVEntities db = new NVEntities())
            {
                try
                {
                    //List<MovimientoTPVDTO> movimientos = await db.MovimientosTPV
                    //    .Where(m => m.FechaCaptura == fechaCaptura && m.ModoCaptura == tipoDatafono)
                    //    .Select(m => new MovimientoTPVDTO
                    //    {
                    //        ModoCaptura = m.ModoCaptura,
                    //        Sesion = m.Sesion,
                    //        Terminal = m.Terminal,
                    //        FechaCaptura = m.FechaCaptura,
                    //        FechaOperacion = m.FechaOperacion,
                    //        ImporteOperacion = m.ImporteOperacion,
                    //        ImporteComision = m.ImporteComision,
                    //        ImporteAbono = m.ImporteAbono,
                    //        CodigoMoneda = m.CodigoMoneda,
                    //        Comentarios = m.Comentarios,
                    //        FechaCreacion = m.FechaCreacion,
                    //        Usuario = m.Usuario
                    //    })
                    //    .ToListAsync()
                    //    .ConfigureAwait(false);

                    var movimientosQuery = db.MovimientosTPV
                        .Where(m => m.FechaCaptura == fechaCaptura)
                        .Where(m =>
                            tipoDatafono == "3"
                                ? m.Terminal.EndsWith("32951570") && m.Terminal.Length >= 11
                                : !m.Terminal.EndsWith("32951570") || m.Terminal.Length < 11
                        );

                    var movimientos = await movimientosQuery
                        .Select(m => new MovimientoTPVDTO
                        {
                            ModoCaptura = m.ModoCaptura,
                            Sesion = m.Sesion,
                            Terminal = m.Terminal,
                            FechaCaptura = m.FechaCaptura,
                            FechaOperacion = m.FechaOperacion,
                            ImporteOperacion = m.ImporteOperacion,
                            ImporteComision = m.ImporteComision,
                            ImporteAbono = m.ImporteAbono,
                            CodigoMoneda = m.CodigoMoneda,
                            Comentarios = m.Comentarios,
                            FechaCreacion = m.FechaCreacion,
                            Usuario = m.Usuario
                        })
                        .ToListAsync()
                        .ConfigureAwait(false);

                    foreach (MovimientoTPVDTO movimiento in movimientos)
                    {
                        movimiento.TextoModoCaptura = MovimientoTPVDTO.GetModoCapturaDescription(movimiento.ModoCaptura);
                        movimiento.UsuarioTerminal = ObtenerUsuarioTerminal(movimiento.Terminal);
                    }

                    return movimientos;
                }
                catch (Exception ex)
                {
                    throw new Exception("No se han podido leer los movimientos de los TPV", ex);
                }

            }
        }
        // NestoAPI#231: valores por defecto (fallback). El mapeo vigente se edita en caliente en el
        // parámetro TerminalesUsuariosTPV (JSON), sin recompilar; ver ObtenerMapaTerminales.
        private readonly Dictionary<string, string> TerminalesUsuarios = new Dictionary<string, string>()
        {
            { "91901505888", "Paloma" }, // antes 91900804273 (dado de baja)
            { "91900804275", "Victoria" },
            { "26617120788", "Laura Camacho" },
            { "00346609775", "Patricia" },
            { "00132951570", "Web" },
            { "00232951570", "Paygold" },
            { "00025537534", "Pilar" },
            { "51570001329", "Bizum tienda online" },
            { "51570002329", "Bizum Paygold" },
            { "00022126270", "Almacén" },
            { "91901357047", "Almacén" },
        };
        internal string ObtenerUsuarioTerminal(string terminal)
        {
            if (ObtenerMapaTerminales().TryGetValue(terminal, out string usuario))
            {
                return usuario;
            }
            return string.Empty; // O un valor predeterminado si prefieres
        }

        // NestoAPI#231: el mapeo terminal→usuario se lee de la tabla TerminalesUsuariosTPV (vía
        // IRepositorioTerminalesTPV), editable sin recompilar. Si la tabla no existe o está vacía,
        // se usa el diccionario por defecto. Se cachea durante la operación (la instancia del
        // servicio es por operación).
        private Dictionary<string, string> ObtenerMapaTerminales()
        {
            if (_mapaTerminalesResuelto != null)
            {
                return _mapaTerminalesResuelto;
            }
            Dictionary<string, string> mapa = _repositorioTerminales?.LeerMapa();
            _mapaTerminalesResuelto = (mapa != null && mapa.Count > 0) ? mapa : TerminalesUsuarios;
            return _mapaTerminalesResuelto;
        }

        public async Task ContabilizarComisionesTarjetas(List<MovimientoTPVDTO> movimientosTPV)
        {
            using (NVEntities db = new NVEntities())
            {
                try
                {
                    List<PreContabilidad> lineas = new List<PreContabilidad>();
                    decimal sumaComisiones = 0;
                    // Si algún día trabajamos con un banco que no sea Caixa tenemos que crear un campo en MovimientoTPVDTO que sea el banco
                    // o si no pasar el banco a este método como un parámetro desde el controlador
                    Banco banco = await db.Bancos.SingleAsync(b => b.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && b.Entidad == "2100");
                    DateTime fechaIngreso = movimientosTPV.First().FechaCaptura.AddDays(1);
                    int numeroCobros = 0;
                    foreach (MovimientoTPVDTO movimiento in movimientosTPV.Where(m => m.ImporteComision != 0))
                    {
                        sumaComisiones += movimiento.ImporteComision;
                        decimal porcentajeComision = movimiento.ImporteComision / movimiento.ImporteOperacion;
                        PreContabilidad nuevaLinea = new PreContabilidad
                        {
                            Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                            Diario = Constantes.DiariosContables.COMISIONES_BANCO,
                            Asiento = 1,
                            Fecha = fechaIngreso,
                            TipoCuenta = Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE,
                            TipoApunte = Constantes.TiposExtractoCliente.SIN_ESPECIFICAR,
                            Nº_Cuenta = Constantes.Cuentas.COMISIONES_BANCO_COBRO_TPV,
                            Concepto = $"Comisiones TPV {banco.Descripción.Trim()} ({porcentajeComision:p})",
                            Nº_Documento = movimiento.Terminal.Length > 10 ? movimiento.Terminal.Substring(movimiento.Terminal.Length - 10) : movimiento.Terminal,
                            Debe = movimiento.ImporteComision,
                            Asiento_Automático = true,
                            CentroCoste = "CA",
                            Delegación = "ALG",
                            Departamento = "ADM",
                            Usuario = movimiento.Usuario,
                            Fecha_Modificación = DateTime.Now
                        };
                        lineas.Add(nuevaLinea);
                        numeroCobros++;
                    }
                    PreContabilidad contrapartida = new PreContabilidad
                    {
                        Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                        Diario = Constantes.DiariosContables.COMISIONES_BANCO,
                        Asiento = 1,
                        Fecha = fechaIngreso,
                        TipoCuenta = Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE,
                        TipoApunte = Constantes.TiposExtractoCliente.SIN_ESPECIFICAR,
                        Nº_Cuenta = banco.Cuenta_Contable,
                        Concepto = $"Comisiones TPV {banco.Descripción.Trim()} ({numeroCobros} cobros)",
                        Nº_Documento = $"TPV_{fechaIngreso:ddMMyy}",
                        Haber = sumaComisiones,
                        Asiento_Automático = true,
                        Delegación = "ALG",
                        Usuario = movimientosTPV.First().Usuario,
                        Fecha_Modificación = DateTime.Now
                    };
                    lineas.Add(contrapartida);

                    _ = await CrearLineasYContabilizarDiario(lineas);
                }
                catch (Exception ex)
                {
                    throw new Exception("Error al contabilizar las comisiones de las tarjetas", ex);
                }
            }
        }

        public async Task<int> NumeroRecibosRemesa(int remesa)
        {
            using (NVEntities db = new NVEntities())
            {
                IQueryable<ExtractoCliente> recibos = db.ExtractosCliente.Where(e => e.TipoApunte == Constantes.TiposExtractoCliente.PAGO && e.Remesa == remesa);
                int numeroRecibos = await recibos.CountAsync();
                return numeroRecibos;
            }
        }


        public async Task<string> LeerProveedorPorNif(string nifProveedor)
        {
            using (NVEntities db = new NVEntities())
            {
                string proveedor = (await db.Proveedores.SingleOrDefaultAsync(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.CIF_NIF.Contains(nifProveedor)))?.Número;
                return string.IsNullOrEmpty(proveedor) ? string.Empty : proveedor.Trim();
            }
        }

        public async Task<string> LeerProveedorPorNombre(string nombreProveedor)
        {
            using (NVEntities db = new NVEntities())
            {
                // nombreProveedor = LimpiarNombreProveedor(nombreProveedor); // quitar SL, SA, etc...
                string proveedor = (await db.Proveedores.SingleOrDefaultAsync(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Nombre.Contains(nombreProveedor)))?.Número;
                return string.IsNullOrEmpty(proveedor) ? string.Empty : proveedor.Trim();
            }
        }

        public async Task<ExtractoProveedorDTO> PagoPendienteUnico(string proveedor, decimal importe)
        {
            using (NVEntities db = new NVEntities())
            {
                ExtractoProveedorDTO pagoPendiente = await db.ExtractosProveedor
                    .AsNoTracking()
                    .Where(e =>
                        e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO &&
                        e.Número == proveedor &&
                        e.ImportePdte == importe
                    )
                    .Select(e => new ExtractoProveedorDTO
                    {
                        Id = e.NºOrden,
                        Empresa = e.Empresa.Trim(),
                        Proveedor = e.Número.Trim(),
                        Contacto = e.Contacto.Trim(),
                        Documento = e.NºDocumento.Trim(),
                        DocumentoProveedor = e.NºDocumentoProv.Trim(),
                        Delegacion = e.Delegación.Trim(),
                        FormaVenta = e.FormaVenta.Trim()
                    })
                    .SingleOrDefaultAsync();

                return pagoPendiente;
            }
        }

        //public async Task<bool> PuntearPorImporte(string empresa, string cuenta, decimal importe)
        //{
        //    try
        //    {
        //        using (var db = new NVEntities())
        //        {
        //            var movimientoSinPuntearDebe = await db.Contabilidades
        //                .SingleAsync(c => c.Empresa == empresa && c.Nº_Cuenta == cuenta && c.Debe == importe && c.Punteado == false);
        //            var movimientoSinPuntearHaber = await db.Contabilidades
        //                .SingleAsync(c => c.Empresa == empresa && c.Nº_Cuenta == cuenta && c.Haber == importe && c.Punteado == false);
        //            movimientoSinPuntearDebe.Punteado = true;
        //            movimientoSinPuntearHaber.Punteado = true;
        //            int movimientosModificados = await db.SaveChangesAsync();
        //            return movimientosModificados == 2; // Comprobamos que se hayan punteado los dos
        //        }                
        //    }
        //    catch
        //    {
        //        return false;
        //    }
        //}

        public async Task<bool> PuntearPorImporte(string empresa, string cuenta, decimal importe)
        {
            try
            {
                using (NVEntities db = new NVEntities())
                {
                    using (DbContextTransaction transaction = db.Database.BeginTransaction())
                    {
                        try
                        {
                            Models.Contabilidad movimientoSinPuntearDebe = await db.Contabilidades
                                .SingleAsync(c => c.Empresa == empresa && c.Nº_Cuenta == cuenta && c.Debe == importe && c.Punteado == false);
                            Models.Contabilidad movimientoSinPuntearHaber = await db.Contabilidades
                                .SingleAsync(c => c.Empresa == empresa && c.Nº_Cuenta == cuenta && c.Haber == importe && c.Punteado == false);

                            // Crear el comando para el primer movimiento
                            System.Data.Common.DbCommand commandDebe = db.Database.Connection.CreateCommand();
                            commandDebe.CommandText = "EXEC [dbo].[prdPuntearContabilidad] @NºOrden, @Punteado";
                            _ = commandDebe.Parameters.Add(new SqlParameter("@NºOrden", movimientoSinPuntearDebe.Nº_Orden));
                            _ = commandDebe.Parameters.Add(new SqlParameter("@Punteado", true));

                            // Crear el comando para el segundo movimiento
                            System.Data.Common.DbCommand commandHaber = db.Database.Connection.CreateCommand();
                            commandHaber.CommandText = "EXEC [dbo].[prdPuntearContabilidad] @NºOrden, @Punteado";
                            _ = commandHaber.Parameters.Add(new SqlParameter("@NºOrden", movimientoSinPuntearHaber.Nº_Orden));
                            _ = commandHaber.Parameters.Add(new SqlParameter("@Punteado", true));

                            // Abrir la conexión si no está abierta
                            if (db.Database.Connection.State != ConnectionState.Open)
                            {
                                await db.Database.Connection.OpenAsync();
                            }

                            // Ejecutar los comandos dentro de la transacción
                            commandDebe.Transaction = transaction.UnderlyingTransaction;
                            _ = await commandDebe.ExecuteNonQueryAsync();

                            commandHaber.Transaction = transaction.UnderlyingTransaction;
                            _ = await commandHaber.ExecuteNonQueryAsync();

                            // Confirmar la transacción
                            transaction.Commit();
                            return true;
                        }
                        catch
                        {
                            // Revertir la transacción en caso de error. #291: rollback seguro,
                            // si lanzara se saltaría el return false y saldría el error del
                            // rollback en vez del resultado de negocio.
                            transaction.RollbackSeguro();
                            return false;
                        }
                    }
                }
            }
            catch
            {
                return false;
            }
        }

    }
}