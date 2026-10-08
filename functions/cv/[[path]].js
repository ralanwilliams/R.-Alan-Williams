// /cv/{lang}.{format} and /cv/v/{version}/{lang}.{format} (ADR 0003 §4). All /cv URLs share one handler.
export { onRequest } from '../../src/Cv.Public/pages.js';
