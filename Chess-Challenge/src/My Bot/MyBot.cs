using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;

public class MyBot : IChessBot {

    // Piece values: null, pawn, knight, bishop, rook, queen, king
    // Base values : { 0, 100, 320, 330, 500, 900, 10000 }
    // Max number of controlled square : 0, 2, 8, 13, 14, 27, 8
    int[] pieceValues = { 0, 50, 160, 165, 300, 800, 10000 };
    int[] pointPerControlledSquare = { 0, 23, 18, 12, 14, 4, 0 };
    // Avg controlled sqr : 0, 2, 5, 8, 9, 15, 8
    // Avg values : 0, 100, 260, 269, 426, 860, 10000

    bool myColorIsWhite = true;
    int bigNumber = 50000;
    Move moveToPlay;
    int node;
    int timeLimit;

    struct TTEntry {
        public ulong key;
        public Move move;
        public sbyte depth, bound;
        public int score;
        public TTEntry(ulong _key, Move _move, sbyte _depth, int _score, sbyte _bound) {
            key = _key; move = _move; depth = _depth; score = _score; bound = _bound;
        }
    }

    static ulong TTMask = 0x7FFFFF; //4.7 million entries, likely consuming about 151 MB
    TTEntry[] TTtable = new TTEntry[TTMask + 1];

    //To access
    //ref TTEntry entry = ref TTtable[key & TTMask];

    //you also need to check to make sure that the stored hash
    //is equal to the board state you're interested in,
    //alongside your normal checks w.r.t. depth etc.

    public Move Think(Board board, Timer timer) {
        myColorIsWhite = board.IsWhiteToMove;
        moveToPlay = board.GetLegalMoves()[0];
        timeLimit = timer.MillisecondsRemaining / 35;

        //---------------------------
        Console.WriteLine("--------");
        //----------------------------

        for(int depth = 1; depth <= 50; depth++) {
            node = 0;
            int timeBefore = timer.MillisecondsElapsedThisTurn;

            Search(board, timer, depth, -bigNumber, bigNumber, 0);

            Console.WriteLine("[MyBot] depth : " + depth + " time : " + (timer.MillisecondsElapsedThisTurn - timeBefore) + " node : " + node + " NPS : " + node/((float)(timer.MillisecondsElapsedThisTurn - timeBefore)/1000f));

            if(timer.MillisecondsElapsedThisTurn >= timeLimit) break;
        }

        return moveToPlay;
    }

    int Search (Board board, Timer timer, int depth, int alpha, int beta, int ply) {
        node++;
        ulong key = board.ZobristKey;
        bool qsearch = depth <= 0;
        bool notRoot = ply > 0;
        int eval = -bigNumber;

        if(notRoot && board.IsRepeatedPosition()) return 0;

        ref TTEntry entry = ref TTtable[key & TTMask];
        
        if(notRoot && entry.key == key && entry.depth >= (sbyte)depth && (
            entry.bound == 3 // exact score
                || entry.bound == 2 && entry.score >= beta // lower bound, fail high
                || entry.bound == 1 && entry.score <= alpha // upper bound, fail low
        )) return entry.score;
        
        int staticEval = EvalPosition(board);

        if(qsearch) {
            eval = staticEval;
            if(eval >= beta) return eval;
            alpha = (eval > alpha)?eval:alpha;
        }

        Move[] moves = board.GetLegalMoves(qsearch);

        if(moves.Length == 0) {
            if(board.IsInCheck()) return -(bigNumber - ply - 1);
            if(qsearch) return staticEval;
            return 0;
        }

        int[] scores = getRankList(board, ref entry, moves);

        bool alphaHasImproved = false;
        Move bestMove = Move.NullMove;
        
        for(int i = 0; i < moves.Length; i++) {
            // Incrementally sort moves
            for(int j = i + 1; j < moves.Length; j++) {
                if(scores[j] > scores[i])
                    (scores[i], scores[j], moves[i], moves[j]) = (scores[j], scores[i], moves[j], moves[i]);
            }

            Move move = moves[i];

            board.MakeMove(move);
            eval = -Search(board, timer, depth - 1, -beta, -alpha, ply + 1);
            board.UndoMove(move);

            //if(ply == 0) Console.WriteLine(move + " Ranking : " + scores[i] + " Eval : " + eval);

            if(eval >= beta) break;

            if(eval > alpha) {
                alphaHasImproved = true;
                alpha = eval;
                if(ply == 0) moveToPlay = move;
                bestMove = move;
            }

            if(ply == 0 && timer.MillisecondsElapsedThisTurn >= timeLimit) break;
        }

        int bound = eval >= beta ? 2 : alphaHasImproved ? 3 : 1;
        entry = new TTEntry(key, bestMove, (sbyte)depth, eval, (sbyte)bound);

        if(bound == 2) return beta;

        return alpha;
    }

    
    int EvalPosition(Board board) {
        PieceList[] pieceLists = board.GetAllPieceLists();
        int material = 0;

        foreach(PieceList pieceList in pieceLists){
            foreach(Piece piece in pieceList) {
                ulong pieceAttack = BitboardHelper.GetPieceAttacks(piece.PieceType, piece.Square, ((piece.IsWhite)?(board.WhitePiecesBitboard):(board.BlackPiecesBitboard)), piece.IsWhite);
                material += (pieceValues[(int)piece.PieceType] + (BitboardHelper.GetNumberOfSetBits(pieceAttack) * pointPerControlledSquare[(int)piece.PieceType]) ) * ((piece.IsWhite == board.IsWhiteToMove) ? 1 : -1);
            }
        }

        return material;
    }
    

    /*
    public int EvalPosition(Board board) {
        PieceList[] pieceLists = board.GetAllPieceLists();
        int material = 0;

        foreach(PieceList pieceList in pieceLists){
            material += pieceValues[(int)pieceList.TypeOfPieceInList] * pieceList.Count * (pieceList.IsWhitePieceList == board.IsWhiteToMove ? 1 : -1);
        }

        
        material += board.GetLegalMoves().Length;
        board.ForceSkipTurn();
        material -= board.GetLegalMoves().Length;
        board.UndoSkipTurn();
        
        
        return material;
    }
    */

    int[] getRankList(Board board, ref TTEntry entry, Move[] moves) {
        int[] scores = new int[moves.Length];

        for(int i = 0; i < moves.Length; i++) {
            Move move = moves[i];
            
            if(move == entry.move) scores[i] = 1000000;
            else scores[i] = RankMove(board, moves[i]);
        }

        return scores;
    }    

    // 1000 : unprotected piece
    // X0 : capture
    // X : attack
    int RankMove(Board board, Move move) {
        return ((move.IsCapture)?
            ((!board.SquareIsAttackedByOpponent(move.TargetSquare))?1000:(int)move.CapturePieceType * (7 - (int)move.MovePieceType) * 10): (
                BitboardHelper.GetNumberOfSetBits(BitboardHelper.GetPieceAttacks(move.MovePieceType, move.TargetSquare, board.AllPiecesBitboard, board.IsWhiteToMove) & ((board.IsWhiteToMove)?board.BlackPiecesBitboard:board.WhitePiecesBitboard))
            )
        );
    }
    

    /*
    int RankMove(Board board, Move move) {
        return ((move.IsCapture)?
            ((int)move.CapturePieceType * (7 - (int)move.MovePieceType) * 10):0
        );
    }
    */
    
}