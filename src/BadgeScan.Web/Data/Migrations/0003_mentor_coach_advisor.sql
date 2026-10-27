-- People who scan in without being on a team: mentors, coaches and advisors. Their
-- team_number holds a code instead of a team: -1 mentor, -2 coach, -3 advisor
-- (Models/Groups.cs). Each role then works like a team of its own, so the one-check-in-a-day
-- and one-badge-per-group rules need no change. The JSON gives them "team": null and a "role".
ALTER TABLE badges
  ADD CONSTRAINT badges_team_or_role CHECK (team_number > 0 OR team_number IN (-1, -2, -3));
